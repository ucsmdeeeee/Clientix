using ClientiX.BotGateway;
using Serilog;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// Serilog для красивых логов в консоли
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Telegram Bot Client как singleton
builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var token = builder.Configuration["Telegram:MainBotToken"]
        ?? throw new InvalidOperationException(
            "Не задан токен главного бота. Положите его в appsettings.Development.json " +
            "в секцию Telegram:MainBotToken");
    return new TelegramBotClient(token);
});

// Фоновый сервис long polling
builder.Services.AddHostedService<TelegramPollingService>();

// Базовые сервисы Web API (понадобятся позже для webhook)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health-check для будущих скриншотов и мониторинга
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "ClientiX.BotGateway",
    timestamp = DateTime.UtcNow
}));

app.MapGet("/", () => "ClientiX BotGateway is running. Telegram polling active.");

try
{
    Log.Information("=== ClientiX.BotGateway starting ===");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "BotGateway crashed");
}
finally
{
    Log.CloseAndFlush();
}