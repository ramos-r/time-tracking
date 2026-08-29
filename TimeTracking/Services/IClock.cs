namespace TimeTracking.Services;

/// <summary>
/// Abstração mínima sobre a hora atual, permitindo que os testes do timer (Seção 47/54)
/// controlem timestamps exatos sem depender de sleeps reais.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
