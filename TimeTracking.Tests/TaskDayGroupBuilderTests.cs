using TimeTracking.Models;
using TimeTracking.Services;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Tests;

/// <summary>
/// Testes da Seção 68 (agrupamento retrátil por data) — cobre exatamente os cenários
/// listados em "Testes a adicionar" da especificação. Usa DomainTask/TimeEntry criados
/// diretamente em memória (sem banco): o algoritmo de agrupamento é puro, não depende de
/// persistência (Seção 65, prioridade de Simplicidade sobre Testabilidade indireta).
/// </summary>
public class TaskDayGroupBuilderTests
{
    private static readonly DateTime BaseUtc = new(2026, 8, 29, 15, 0, 0, DateTimeKind.Utc);

    private static DomainTask CreateTask(int id, string name, params TimeEntry[] entries)
    {
        var task = new DomainTask { Id = id, Name = name, CreatedAt = BaseUtc, UpdatedAt = BaseUtc };
        foreach (var entry in entries)
        {
            task.TimeEntries.Add(entry);
        }

        return task;
    }

    private static TimeEntry ClosedEntry(DateTime startedAtUtc, TimeSpan duration) =>
        new() { StartedAt = startedAtUtc, EndedAt = startedAtUtc + duration };

    [Fact]
    public void Two_Tasks_With_Sessions_On_The_Same_Day_Sum_Correctly()
    {
        var taskA = CreateTask(1, "Tarefa A", ClosedEntry(BaseUtc, TimeSpan.FromMinutes(30)));
        var taskB = CreateTask(2, "Tarefa B", ClosedEntry(BaseUtc.AddHours(1), TimeSpan.FromMinutes(45)));

        var groups = TaskDayGroupBuilder.Build([taskA, taskB], BaseUtc);
        var today = groups.Single(g => g.IsToday);

        Assert.Equal(TimeSpan.FromMinutes(75), today.TotalDuration);
        Assert.Equal(2, today.Tasks.Count);
    }

    [Fact]
    public void Tasks_On_Different_Days_Produce_Separate_Groups_With_Own_Totals()
    {
        var yesterday = CreateTask(1, "Ontem", ClosedEntry(BaseUtc.AddDays(-1), TimeSpan.FromHours(2)));
        var today = CreateTask(2, "Hoje", ClosedEntry(BaseUtc, TimeSpan.FromHours(1)));

        var groups = TaskDayGroupBuilder.Build([yesterday, today], BaseUtc);

        Assert.Equal(2, groups.Count);
        var todayGroup = groups.Single(g => g.IsToday);
        var yesterdayGroup = groups.Single(g => !g.IsToday);

        Assert.Equal(TimeSpan.FromHours(1), todayGroup.TotalDuration);
        Assert.Single(todayGroup.Tasks);

        Assert.Equal(TimeSpan.FromHours(2), yesterdayGroup.TotalDuration);
        Assert.Single(yesterdayGroup.Tasks);
    }

    [Fact]
    public void Open_TimeEntry_Uses_Now_For_The_Day_Total_Without_Error()
    {
        var runningStart = BaseUtc.AddMinutes(-20);
        var task = CreateTask(1, "Rodando", new TimeEntry { StartedAt = runningStart, EndedAt = null });

        var groups = TaskDayGroupBuilder.Build([task], BaseUtc);
        var today = groups.Single(g => g.IsToday);

        Assert.Equal(TimeSpan.FromMinutes(20), today.TotalDuration);
    }

    [Fact]
    public void Task_Without_Any_TimeEntry_Is_Placed_In_Todays_Group()
    {
        var newTask = CreateTask(1, "Recém-criada");

        var groups = TaskDayGroupBuilder.Build([newTask], BaseUtc);
        var today = groups.Single(g => g.IsToday);

        Assert.Contains(today.Tasks, t => t.Id == newTask.Id);
        Assert.Equal(TimeSpan.Zero, today.TotalDuration);
    }

    [Fact]
    public void Task_With_Sessions_On_Multiple_Days_Is_Placed_Under_The_Most_Recent_Day()
    {
        var task = CreateTask(1, "Multi-dia",
            ClosedEntry(BaseUtc.AddDays(-3), TimeSpan.FromHours(1)),
            ClosedEntry(BaseUtc, TimeSpan.FromMinutes(10)));

        var groups = TaskDayGroupBuilder.Build([task], BaseUtc);
        var todayGroup = groups.Single(g => g.IsToday);
        var olderGroup = groups.Single(g => g.Date == BaseUtc.AddDays(-3).Date);

        Assert.Contains(todayGroup.Tasks, t => t.Id == task.Id);
        Assert.DoesNotContain(olderGroup.Tasks, t => t.Id == task.Id);

        // O total do cabeçalho do dia mais antigo continua contabilizando a sessão daquele
        // dia, mesmo que o card da tarefa não apareça mais ali (Seção 68 — cada TimeEntry
        // conta para o dia do seu próprio StartedAt, independente de onde o card aparece).
        Assert.Equal(TimeSpan.FromHours(1), olderGroup.TotalDuration);
    }

    [Fact]
    public void Entry_Crossing_Midnight_Counts_Toward_The_Start_Day()
    {
        // Construído a partir de um horário local (não UTC fixo) para o teste ser
        // determinístico independentemente do fuso horário da máquina que o executa.
        var startLocal = DateTime.Today.AddHours(23).AddMinutes(30);
        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        var nowUtc = startUtc.AddHours(2); // já passou da meia-noite local

        var task = CreateTask(1, "Atravessa meia-noite", ClosedEntry(startUtc, TimeSpan.FromHours(1)));

        var groups = TaskDayGroupBuilder.Build([task], nowUtc);
        var startDayGroup = groups.SingleOrDefault(g => g.Date == startLocal.Date);

        Assert.NotNull(startDayGroup);
        Assert.Equal(TimeSpan.FromHours(1), startDayGroup!.TotalDuration);
    }
}
