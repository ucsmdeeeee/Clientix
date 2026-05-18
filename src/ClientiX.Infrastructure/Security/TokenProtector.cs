using Microsoft.AspNetCore.DataProtection;

namespace ClientiX.Infrastructure.Security;

/// <summary>
/// Сервис шифрования и расшифровки токенов Telegram-ботов мастеров.
/// Использует стандартный механизм Data Protection платформы .NET 8.
/// Ключи шифрования хранятся в файловой системе на сервере.
/// </summary>
public class TokenProtector
{
    private readonly IDataProtector _protector;

    public TokenProtector(IDataProtectionProvider provider)
    {
        // Purpose-строка изолирует наш protector от других применений в системе
        _protector = provider.CreateProtector("ClientiX.BotToken.v1");
    }

    public string Encrypt(string plainToken) => _protector.Protect(plainToken);

    public string Decrypt(string encryptedToken) => _protector.Unprotect(encryptedToken);
}