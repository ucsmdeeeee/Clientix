using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClientiX.Infrastructure.Persistence;

/// <summary>
/// Фабрика для создания DbContext на этапе проектирования (миграции EF Core).
/// Используется командой dotnet ef. В runtime не применяется.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClientiXDbContext>
{
    public ClientiXDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClientiXDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5433;Database=clientix;Username=clientix;Password=postgres"
        );
        return new ClientiXDbContext(optionsBuilder.Options);
    }
}