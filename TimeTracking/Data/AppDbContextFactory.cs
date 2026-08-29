using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TimeTracking.Helpers;

namespace TimeTracking.Data;

/// <summary>
/// Usada apenas pelas ferramentas de design-time do EF Core (dotnet-ef) para gerar migrations.
/// Não faz parte do fluxo de execução da aplicação (ver composition root em App.xaml.cs).
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={DatabasePathProvider.GetDatabasePath()}");

        return new AppDbContext(optionsBuilder.Options);
    }
}
