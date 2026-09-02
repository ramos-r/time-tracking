using TimeTracking.Services;

namespace TimeTracking.Tests;

/// <summary>Formato do rótulo de data relativa nos cabeçalhos de grupo (Seção 68 — ajuste
/// visual, item 5): "Hoje"/"Ontem" ganham a data numérica DD/MM; dias mais antigos mantêm
/// o formato completo já existente ("Seg, dd/MM/yyyy").</summary>
public class RelativeDateFormatterTests
{
    private static readonly DateTime Today = new(2026, 9, 1);

    [Fact]
    public void Today_Includes_Numeric_Date()
    {
        Assert.Equal("Hoje, 01/09", RelativeDateFormatter.Format(Today, Today));
    }

    [Fact]
    public void Yesterday_Includes_Numeric_Date()
    {
        Assert.Equal("Ontem, 31/08", RelativeDateFormatter.Format(Today.AddDays(-1), Today));
    }

    [Fact]
    public void Older_Dates_Keep_The_Existing_Weekday_Format()
    {
        var olderDate = new DateTime(2026, 2, 27);
        string[] weekdayAbbreviations = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
        var expectedWeekday = weekdayAbbreviations[(int)olderDate.DayOfWeek];

        Assert.Equal($"{expectedWeekday}, 27/02/2026", RelativeDateFormatter.Format(olderDate, Today));
    }
}
