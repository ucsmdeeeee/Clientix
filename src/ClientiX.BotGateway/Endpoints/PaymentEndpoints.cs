using ClientiX.Infrastructure.Repositories;
using Telegram.Bot;

namespace ClientiX.BotGateway.Endpoints;

/// <summary>
/// HTTP-эндпоинты для платежей.
/// /stub-pay/{id} — экран-заглушка вместо страницы ЮKassa (для разработки)
/// /yk-webhook — приёмник уведомлений от ЮKassa о статусе платежа
/// </summary>
public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        // Заглушка страницы оплаты: показывает HTML с кнопкой «Подтвердить» и «Отменить»
        app.MapGet("/stub-pay/{id:long}", (long id) =>
        {
            var html = $$"""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8">
              <title>ClientiX — Оплата (тест)</title>
              <style>
                body { font-family: -apple-system, system-ui, sans-serif; max-width: 480px; margin: 60px auto; padding: 20px; }
                .card { background: #fff; border: 1px solid #e1e4e8; border-radius: 12px; padding: 28px; box-shadow: 0 2px 8px rgba(0,0,0,.06); }
                h1 { margin: 0 0 8px; font-size: 22px; }
                .muted { color: #6a737d; font-size: 14px; }
                .badge { background: #fff3cd; color: #856404; padding: 8px 12px; border-radius: 6px; font-size: 13px; margin: 16px 0; }
                button { width: 100%; padding: 14px; font-size: 15px; border-radius: 8px; border: 0; cursor: pointer; margin-top: 12px; }
                .pay { background: #5469d4; color: #fff; }
                .pay:hover { background: #4356b8; }
                .cancel { background: #f1f3f5; color: #495057; }
              </style>
            </head>
            <body>
              <div class="card">
                <h1>💳 Оплата подписки</h1>
                <div class="muted">Платёж #{{id}}</div>
                <div class="badge">⚠️ Это тестовый режим. Реальные платежи не проводятся.</div>
                <p>Нажмите «Оплатить», чтобы имитировать успешное прохождение платежа через ЮKassa и активировать подписку.</p>
                <form method="POST" action="/stub-pay/{{id}}/confirm">
                  <button type="submit" class="pay">✓ Оплатить</button>
                </form>
                <form method="POST" action="/stub-pay/{{id}}/cancel">
                  <button type="submit" class="cancel">Отменить</button>
                </form>
              </div>
            </body>
            </html>
            """;
            return Results.Content(html, "text/html; charset=utf-8");
        });

        // Подтверждение оплаты (имитация успешного webhook от ЮKassa)
        app.MapPost("/stub-pay/{id:long}/confirm", async (
            long id,
            PaymentRepository payments,
            ITelegramBotClient bot,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var payment = await payments.GetByIdAsync(id, ct);
            if (payment is null) return Results.NotFound();

            var ok = await payments.MarkAsSucceededAndExtendSubscriptionAsync(
                id, $"stub-{Guid.NewGuid():N}", ct);

            if (ok)
            {
                logger.LogInformation(
                    "Платёж {PaymentId} подтверждён (заглушка), подписка продлена для user={UserId}",
                    id, payment.UserId);

                // Уведомляем пользователя в Telegram
                try
                {
                    await bot.SendMessage(
                        chatId: payment.User.TelegramId,
                        text:
                            $"✅ Оплата прошла успешно!\n\n" +
                            $"📅 Подписка активна до: {payment.User.Subscription?.CurrentPeriodEnd:dd.MM.yyyy}\n" +
                            $"💰 Сумма: {payment.AmountRub} руб.\n\n" +
                            $"Спасибо за доверие к ClientiX!",
                        cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Не удалось отправить уведомление об оплате");
                }
            }

            // Редиректим обратно в бот
            return Results.Content("""
                <html><body style="font-family:sans-serif;text-align:center;padding:60px">
                <h2>✅ Платёж проведён</h2>
                <p>Вернитесь в Telegram-бот для продолжения работы.</p>
                <p><a href="https://t.me/cl1ent1x_bot">Открыть бот</a></p>
                </body></html>
                """, "text/html; charset=utf-8");
        });

        app.MapPost("/stub-pay/{id:long}/cancel", (long id) =>
        {
            return Results.Content("""
                <html><body style="font-family:sans-serif;text-align:center;padding:60px">
                <h2>❌ Платёж отменён</h2>
                <p>Вернитесь в Telegram-бот, чтобы выбрать другой тариф.</p>
                <p><a href="https://t.me/cl1ent1x_bot">Открыть бот</a></p>
                </body></html>
                """, "text/html; charset=utf-8");
        });

        // Реальный webhook от ЮKassa — будет включён после получения ключей
        app.MapPost("/yk-webhook", async (
            HttpContext ctx,
            PaymentRepository payments,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);

            logger.LogInformation("Получен webhook ЮKassa: {Body}", body);

            // TODO: распарсить JSON, найти Payment по metadata.payment_id,
            // вызвать MarkAsSucceededAndExtendSubscriptionAsync с реальным yk_payment_id

            return Results.Ok(new { received = true });
        });
    }
}