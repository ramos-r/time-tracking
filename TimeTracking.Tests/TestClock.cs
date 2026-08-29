using TimeTracking.Services;

namespace TimeTracking.Tests;

/// <summary>Relógio controlável para testes determinísticos do timer (Seção 47/54) —
/// evita depender de sleeps reais para validar cálculos de tempo.</summary>
public class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
}
