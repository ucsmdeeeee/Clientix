namespace ClientiX.BotGateway.MasterBots;

/// <summary>
/// Hosted-сервис, который при запуске приложения поднимает все активные боты мастеров.
/// </summary>
public class MasterBotsHostedService : IHostedService
{
    private readonly MasterBotManager _manager;
    private readonly ILogger<MasterBotsHostedService> _logger;

    public MasterBotsHostedService(
        MasterBotManager manager, ILogger<MasterBotsHostedService> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запуск менеджера ботов мастеров...");
        await _manager.StartAllAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _manager.StopAll();
        return Task.CompletedTask;
    }
}