using ClientiX.BotGateway;
using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using ClientiX.Infrastructure.State;
using ClientiX.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using Telegram.Bot;
using Microsoft.AspNetCore.DataProtection;
using ClientiX.BotGateway.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Telegram Bot Client
builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var token = builder.Configuration["Telegram:MainBotToken"]
        ?? throw new InvalidOperationException("Не задан токен главного бота.");
    return new TelegramBotClient(token);
});

// DbContext
builder.Services.AddDbContext<ClientiXDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Репозитории
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<PaymentRepository>();

// Redis
var redisConn = builder.Configuration["Redis:Connection"]
    ?? throw new InvalidOperationException("Не задана строка подключения к Redis");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConn));

// FSM состояния
builder.Services.AddSingleton<UserStateService>();

// Data Protection для шифрования токенов ботов мастеров
builder.Services.AddDataProtection()
    .SetApplicationName("ClientiX")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, ".keys")));

builder.Services.AddSingleton<TokenProtector>();

// Главный поллинг-сервис должен быть ПОСЛЕДНИМ — он зависит от всего выше
builder.Services.AddHostedService<TelegramPollingService>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ClientiX.Infrastructure.Payments.YooKassaPaymentService>();

// Web API stuff
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "ClientiX.BotGateway",
    timestamp = DateTime.UtcNow
}));

app.MapGet("/", () => "ClientiX BotGateway is running. Telegram polling active.");

app.MapPaymentEndpoints();

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