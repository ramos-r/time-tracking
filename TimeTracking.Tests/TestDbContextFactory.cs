using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;

namespace TimeTracking.Tests;

/// <summary>Factory mínima de DbContext para testes, reutilizando as mesmas
/// DbContextOptions (e portanto a mesma conexão SQLite :memory:) em toda a suíte.</summary>
public class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

    public AppDbContext CreateDbContext() => new(_options);
}
