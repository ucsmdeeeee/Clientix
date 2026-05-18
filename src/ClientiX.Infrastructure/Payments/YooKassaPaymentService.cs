using ClientiX.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace ClientiX.Infrastructure.Payments;

/// <summary>
/// Сервис создания платежей через ЮKassa.
/// На этапе разработки и в режиме STUB генерирует ссылку-заглушку на собственный endpoint.
/// При наличии реальных ключей переключается на API ЮKassa.
/// </summary>
public class YooKassaPaymentService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public YooKassaPaymentService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _http = httpFactory.CreateClient();
    }

    public async Task<string> CreatePaymentLinkAsync(
        Payment payment, string description, CancellationToken ct)
    {
        var shopId = _config["YooKassa:ShopId"];
        var secret = _config["YooKassa:SecretKey"];
        var returnUrl = _config["YooKassa:ReturnUrl"] ?? "https://t.me/cl1ent1x_bot";

        // STUB: пока нет реальных ключей — отдаём ссылку на нашу заглушку.
        // Когда подключим ЮKassa, изменим только эту проверку.
        bool isStub = string.IsNullOrWhiteSpace(shopId)
            || string.IsNullOrWhiteSpace(secret)
            || shopId.StartsWith("STUB", StringComparison.OrdinalIgnoreCase);

        if (isStub)
        {
            // Ссылка на наш собственный экран «оплата (тест)»
            var publicBase = _config["PublicBaseUrl"] ?? "http://localhost:5000";
            return $"{publicBase}/stub-pay/{payment.Id}";
        }

        // Реальный вызов ЮKassa (будет включён, когда придут ключи)
        throw new NotImplementedException(
            "Реальный вызов ЮKassa подключим, как только модерация будет пройдена.");
    }
}