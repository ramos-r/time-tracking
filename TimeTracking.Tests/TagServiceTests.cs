using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Repositories;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes de TagService (Seção 47: Criar/Editar/Excluir/Associar). O critério de aceite
/// central da Fase 7 — excluir uma tag não deve excluir as tarefas associadas — já tinha
/// cobertura no nível de EF Core puro (Fase 2); aqui a mesma regra é validada passando
/// pela camada de serviço de verdade, e a validação de nome/cor é exercitada.
/// </summary>
public class TagServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly TagService _tagService;
    private readonly TaskService _taskService;

    public TagServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.Migrate();

        var factory = new TestDbContextFactory(_options);
        _tagService = new TagService(new TagRepository(factory));
        _taskService = new TaskService(new TaskRepository(factory));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task CreateAsync_Persists_Tag_With_Timestamps()
    {
        var tag = await _tagService.CreateAsync("Desenvolvimento", "Projetos de programação", "#C89B6D");

        var all = await _tagService.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Desenvolvimento", all[0].Name);
        Assert.Equal("#C89B6D", all[0].Color);
        Assert.True(all[0].CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task CreateAsync_Rejects_Empty_Name()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tagService.CreateAsync("", null, "#C89B6D"));
    }

    [Fact]
    public async Task CreateAsync_Rejects_Invalid_Color()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tagService.CreateAsync("Estudos", null, "azul"));
    }

    [Fact]
    public async Task UpdateAsync_Changes_Name_Description_And_Color()
    {
        var tag = await _tagService.CreateAsync("Estudos", "Faculdade", "#7FAE82");

        await _tagService.UpdateAsync(tag.Id, "Estudos e Cursos", "Faculdade e cursos online", "#6D8FC8");

        var updated = await _tagService.GetByIdAsync(tag.Id);
        Assert.Equal("Estudos e Cursos", updated!.Name);
        Assert.Equal("Faculdade e cursos online", updated.Description);
        Assert.Equal("#6D8FC8", updated.Color);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Tag_But_Keeps_Associated_Task_With_Null_TagId()
    {
        var tag = await _tagService.CreateAsync("Trabalho", null, "#D3A85C");
        var task = await _taskService.CreateAsync("Reunião semanal", null, tag.Id);

        await _tagService.DeleteAsync(tag.Id);

        Assert.Null(await _tagService.GetByIdAsync(tag.Id));

        var reloadedTask = await _taskService.GetByIdAsync(task.Id);
        Assert.NotNull(reloadedTask); // a tarefa continua existindo (Fase 7 — critério de aceite)
        Assert.Null(reloadedTask!.TagId);
    }
}
