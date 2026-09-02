using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Services;

public static class TaskDayGroupBuilder
{
    public static List<DayGroupData> Build(IReadOnlyList<DomainTask> tasks, DateTime nowUtc)
    {
        var todayLocalDate = ToLocalDate(nowUtc);

        var placement = new Dictionary<DateTime, List<DomainTask>>();
        var entryTotals = new Dictionary<DateTime, TimeSpan>();

        foreach (var task in tasks)
        {
            var anchorDate = task.TimeEntries.Count == 0
                ? todayLocalDate
                : ToLocalDate(task.TimeEntries.Max(e => e.StartedAt));

            if (!placement.TryGetValue(anchorDate, out var list))
            {
                list = new List<DomainTask>();
                placement[anchorDate] = list;
            }

            list.Add(task);

            foreach (var entry in task.TimeEntries)
            {
                var entryDate = ToLocalDate(entry.StartedAt);
                var duration = (entry.EndedAt ?? nowUtc) - entry.StartedAt;

                entryTotals[entryDate] = entryTotals.TryGetValue(entryDate, out var existing)
                    ? existing + duration
                    : duration;
            }
        }

        var allDates = new HashSet<DateTime>(placement.Keys);
        allDates.UnionWith(entryTotals.Keys);
        allDates.Add(todayLocalDate);

        var result = new List<DayGroupData>();
        foreach (var date in allDates)
        {
            var groupTasks = placement.TryGetValue(date, out var tasksForDate) ? tasksForDate : new List<DomainTask>();
            var total = entryTotals.TryGetValue(date, out var sum) ? sum : TimeSpan.Zero;
            result.Add(new DayGroupData(date, total, groupTasks, date == todayLocalDate));
        }

        return result.OrderByDescending(g => g.Date).ToList();
    }

    /// <summary>Recalcula o total de um único dia (usado no tick de 1s enquanto um timer
    /// está rodando — Seção 43: sempre em memória, nunca lido do banco a cada tick).</summary>
    public static TimeSpan SumEntriesForDate(IReadOnlyList<DomainTask> tasks, DateTime localDate, DateTime nowUtc)
    {
        var total = TimeSpan.Zero;

        foreach (var task in tasks)
        {
            foreach (var entry in task.TimeEntries)
            {
                if (ToLocalDate(entry.StartedAt) != localDate)
                {
                    continue;
                }

                total += (entry.EndedAt ?? nowUtc) - entry.StartedAt;
            }
        }

        return total;
    }

    private static DateTime ToLocalDate(DateTime utcValue) =>
        DateTime.SpecifyKind(utcValue, DateTimeKind.Utc).ToLocalTime().Date;
}

public record DayGroupData(DateTime Date, TimeSpan TotalDuration, List<DomainTask> Tasks, bool IsToday);
