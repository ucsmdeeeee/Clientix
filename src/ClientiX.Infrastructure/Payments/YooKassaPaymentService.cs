using ClientiX.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Yandex.Checkout.V3;

using Payment = ClientiX.Domain.Entities.Payment;

namespace ClientiX.Infrastructure.Payments;

/// <summary>
/// Сервис создания платежей через ЮKassa.
/// В STUB-режиме (нет ключей или ключи начинаются с STUB) возвращает ссылку на собственную страницу-заглушку.
/// В боевом режиме вызывает реальный API ЮKassa через SDK Yandex.Checkout.V3.
/// Парсинг webhook'а реализован напрямую через System.Text.Json, без зависимости от SDK.
/// </summary>
public class YooKassaPaymentService
{
    private readonly IConfiguration _config;
    private readonly ILogger<YooKassaPaymentService> _logger;
    private readonly Yandex.Checkout.V3.AsyncClient? _client;
    private readonly bool _isStub;

    public YooKassaPaymentService(
        IConfiguration config,
        ILogger<YooKassaPaymentService> logger)
    {
        _config = config;
        _logger = logger;

        var shopId = _config["YooKassa:ShopId"];
        var secret = _config["YooKassa:SecretKey"];

        _isStub = string.IsNullOrWhiteSpace(shopId)
            || string.IsNullOrWhiteSpace(secret)
            || shopId.StartsWith("STUB", StringComparison.OrdinalIgnoreCase);

        if (!_isStub)
        {
            _client = new Yandex.Checkout.V3.Client(shopId!, secret!).MakeAsync();
            _logger.LogInformation(
                "YooKassa SDK инициализирован: shopId={ShopId}", shopId);
        }
        else
        {
            _logger.LogWarning(
                "YooKassa в STUB-режиме: реальные платежи не проводятся.");
        }
    }

    /// <summary>
    /// Создаёт платёж и возвращает URL страницы оплаты.
    /// В STUB-режиме — наша локальная страница, в боевом — страница ЮKassa.
    /// </summary>
    public async Task<string> CreatePaymentLinkAsync(
        Payment payment, string description, CancellationToken ct)
    {
        if (_isStub)
        {
            var publicBase = _config["PublicBaseUrl"] ?? "http://localhost:5000";
            return $"{publicBase}/stub-pay/{payment.Id}";
        }

        var returnUrl = _config["YooKassa:ReturnUrl"] ?? "https://t.me/cl1ent1x_bot";

        var request = new Yandex.Checkout.V3.NewPayment
        {
            Amount = new Yandex.Checkout.V3.Amount
            {
                Value = payment.AmountRub,
                Currency = "RUB"
            },
            Capture = true,
            Confirmation = new Yandex.Checkout.V3.Confirmation
            {
                Type = Yandex.Checkout.V3.ConfirmationType.Redirect,
                ReturnUrl = returnUrl
            },
            Description = description,
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = payment.Id.ToString(),
                ["user_id"] = payment.UserId.ToString(),
                ["tariff_plan_id"] = payment.TariffPlanId.ToString()
            }
        };

        // Идемпотентность ЮKassa: повторный запрос с тем же ключом не создаст дубль платежа
        var idempotenceKey = $"clientix-payment-{payment.Id}";

        var created = await _client!.CreatePaymentAsync(request, idempotenceKey, ct);

        _logger.LogInformation(
            "Создан платёж в ЮKassa: yk_id={YkId}, status={Status}, internal_id={InternalId}",
            created.Id, created.Status, payment.Id);

        return created.Confirmation.ConfirmationUrl;
    }

    /// <summary>
    /// Парсит webhook от ЮKassa напрямую через System.Text.Json.
    /// Структура: { "event": "payment.succeeded", "object": { "id": "...", "status": "...", "metadata": { "payment_id": "..." } } }
    /// </summary>
    public ParsedWebhook? ParseWebhook(string body)
    {
        if (_isStub) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            var eventName = root.TryGetProperty("event", out var ev)
                ? ev.GetString() ?? ""
                : "";

            if (!root.TryGetProperty("object", out var obj))
            {
                _logger.LogWarning("Webhook ЮKassa: нет поля object");
                return null;
            }

            var ykPaymentId = obj.TryGetProperty("id", out var idEl)
                ? idEl.GetString() ?? ""
                : "";

            var status = obj.TryGetProperty("status", out var stEl)
                ? stEl.GetString() ?? ""
                : "";

            if (!obj.TryGetProperty("metadata", out var metadata)
                || !metadata.TryGetProperty("payment_id", out var paymentIdEl)
                || !long.TryParse(paymentIdEl.GetString(), out var internalPaymentId))
            {
                _logger.LogWarning(
                    "Webhook ЮKassa: в metadata нет payment_id, yk_id={YkId}",
                    ykPaymentId);
                return null;
            }

            return new ParsedWebhook
            {
                InternalPaymentId = internalPaymentId,
                YooKassaPaymentId = ykPaymentId,
                Status = status.ToLowerInvariant(),
                Event = eventName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось распарсить webhook ЮKassa");
            return null;
        }
    }
}

/// <summary>
/// Результат разбора webhook'а от ЮKassa.
/// </summary>
public class ParsedWebhook
{
    public long InternalPaymentId { get; set; }
    public string YooKassaPaymentId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Event { get; set; } = "";
}