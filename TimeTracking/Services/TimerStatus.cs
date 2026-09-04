namespace TimeTracking.Services;

/// <summary>
/// Estado do timer de uma tarefa, calculado a partir dos timestamps persistidos (Seção 10/43).
/// Não é um estado armazenado — é recalculado sob demanda a partir das TimeEntry existentes.
/// </summary>
public record TimerStatus(bool IsRunning, TimeSpan ClosedEntriesTotal, DateTime? RunningStartedAt, bool HasEntries = false)
{
    /// <summary>Tempo total decorrido, somando as sessões encerradas com a sessão aberta
    /// (se houver), calculado em memória — nunca lido do banco a cada tick (Seção 43).</summary>
    public TimeSpan GetElapsed(DateTime now) =>
        IsRunning && RunningStartedAt.HasValue
            ? ClosedEntriesTotal + (now - RunningStartedAt.Value)
            : ClosedEntriesTotal;
}
