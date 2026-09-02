namespace TimeTracking.Services;

public static class RelativeDateFormatter
{
    private static readonly string[] WeekdayAbbreviations = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

    public static string Format(DateTime date, DateTime today)
    {
        var diffDays = (date.Date - today.Date).Days;

        if (diffDays == 0)
        {
            return $"Hoje, {date:dd/MM}";
        }

        if (diffDays == -1)
        {
            return $"Ontem, {date:dd/MM}";
        }

        var weekday = WeekdayAbbreviations[(int)date.DayOfWeek];
        return $"{weekday}, {date:dd/MM/yyyy}";
    }
}
