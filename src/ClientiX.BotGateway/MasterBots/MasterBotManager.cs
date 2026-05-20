using System.Collections.Concurrent;
using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using ClientiX.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace ClientiX.BotGateway.MasterBots;

/// <summary>
/// Менеджер ботов мастеров. При старте поднимает long polling для всех активных
/// ManagedBot из БД. Позволяет динамически добавлять/останавливать поллер при
/// подключении/отключении бота мастера в @cl1ent1x_bot.
/// </summary>
public class MasterBotManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TokenProtector _protector;
    private readonly ILogger<MasterBotManager> _logger;
    private readonly MasterBotUpdateHandler _updateHandler;

    // userId мастера → его бот
    private readonly ConcurrentDictionary<long, MasterBotContext> _bots = new();

    public MasterBotManager(
        IServiceScopeFactory scopeFactory,
        TokenProtector protector,
        ILogger<MasterBotManager> logger,
        MasterBotUpdateHandler updateHandler)
    {
        _scopeFactory = scopeFactory;
        _protector = protector;
        _logger = logger;
        _updateHandler = updateHandler;
    }

    public IReadOnlyDictionary<long, MasterBotContext> ActiveBots => _bots;

    /// <summary>
    /// Стартует все боты мастеров из БД. Вызывается один раз при запуске приложения.
    /// </summary>
    public async Task StartAllAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientiXDbContext>();

        var activeBots = await db.ManagedBots
            .Where(b => b.IsActive && b.BotTelegramId > 0)
            .ToListAsync(ct);

        _logger.LogInformation("Найдено активных ботов мастеров для запуска: {Count}", activeBots.Count);

        foreach (var bot in activeBots)
        {
            try
            {
                await StartOneAsync(bot.UserId, bot.BotTelegramId, bot.BotUsername, bot.BotTokenEncrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Не удалось запустить бот мастера user={UserId}, bot=@{BotUsername}",
                    bot.UserId, bot.BotUsername);
            }
        }
    }

    /// <summary>
    /// Стартует одного бота мастера. Вызывается при подключении нового бота через @cl1ent1x_bot.
    /// </summary>
    public async Task StartOneAsync(
        long userId, long botTelegramId, string botUsername, string encryptedToken)
    {
        // Если уже запущен — сначала останавливаем
        if (_bots.TryGetValue(userId, out var existing))
        {
            _logger.LogInformation(
                "Бот мастера user={UserId} уже запущен, перезапускаю", userId);
            existing.Cts.Cancel();
            _bots.TryRemove(userId, out _);
        }

        string token;
        try
        {
            token = _protector.Decrypt(encryptedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Не удалось расшифровать токен бота user={UserId}", userId);
            return;
        }

        var client = new TelegramBotClient(token);
        var cts = new CancellationTokenSource();

        // Проверка, что токен валиден — запросим getMe
        try
        {
            var me = await client.GetMe(cts.Token);
            _logger.LogInformation(
                "Бот мастера запущен: @{Username} (id={BotId}, владелец user={UserId})",
                me.Username, me.Id, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Токен бота мастера user={UserId} невалиден, бот не запущен", userId);
            return;
        }

        var ctx = new MasterBotContext
        {
            UserId = userId,
            BotTelegramId = botTelegramId,
            BotUsername = botUsername,
            Client = client,
            Cts = cts
        };

        _bots[userId] = ctx;

        // StartReceiving — fire and forget, работает в фоне до отмены через cts
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
            DropPendingUpdates = true
        };

        client.StartReceiving(
            updateHandler: (b, u, t) => _updateHandler.HandleAsync(ctx, b, u, t),
            errorHandler: (b, ex, src, t) => _updateHandler.HandleErrorAsync(ctx, b, ex, src, t),
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token);
    }

    public void StopOne(long userId)
    {
        if (_bots.TryRemove(userId, out var ctx))
        {
            ctx.Cts.Cancel();
            _logger.LogInformation(
                "Бот мастера остановлен: @{Username} (user={UserId})",
                ctx.BotUsername, userId);
        }
    }

    public void StopAll()
    {
        foreach (var kvp in _bots)
        {
            kvp.Value.Cts.Cancel();
        }
        _bots.Clear();
        _logger.LogInformation("Все боты мастеров остановлены");
    }

    /// <summary>
    /// Обработка фатальной ошибки бота: невалидный токен, бан и т.п.
    /// Останавливаем поллинг, деактивируем в БД, уведомляем мастера.
    /// </summary>
    public async Task HandleBotFatalErrorAsync(long userId, string reason, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Бот мастера user={UserId} помечен как недоступный: {Reason}",
            userId, reason);

        // Останавливаем поллинг
        StopOne(userId);

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();

        await users.DeactivateManagedBotAsync(userId, ct);

        // Уведомляем мастера через главный бот
        try
        {
            var mainBot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            var master = await users.GetByIdAsync(userId, ct);
            if (master is not null)
            {
                await mainBot.SendMessage(
                    chatId: master.TelegramId,
                    text:
                        "⚠️ Ваш бот мастера перестал работать.\n\n" +
                        $"Причина: <i>{System.Net.WebUtility.HtmlEncode(reason)}</i>\n\n" +
                        "Это могло произойти, если:\n" +
                        "▪️ вы сменили токен в @BotFather\n" +
                        "▪️ удалили или приостановили бота\n" +
                        "▪️ забанили бота для главного процесса\n\n" +
                        "Чтобы возобновить работу — отправьте /start и подключите бота заново.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось уведомить мастера {UserId} о невалидном токене", userId);
        }
    }
}