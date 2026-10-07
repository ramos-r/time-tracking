namespace TimeTracking.Services;

/// <summary>Tipo da mudança no timer: uma sessão foi aberta ou encerrada.</summary>
public enum TimerChangeKind
{
    Started,
    Ended
}

/// <summary>Quem causou a mudança no timer. O PomodoroService ignora as mudanças que ele mesmo
/// originou (Seção 70, "Interação com a tela Time Tracking"). É um parâmetro explícito de cada
/// chamada — e não uma flag interna do PomodoroService — porque uma flag se perderia com a
/// reentrância assíncrona (outra ação do usuário podendo chegar no meio de uma chamada).</summary>
public enum TimerOrigin
{
    User,
    Pomodoro
}

/// <summary>Notificação de mudança do TimerService (Seção 34, v1.5.1). Uma troca de tarefa gera
/// dois eventos: Ended da anterior e, em seguida, Started da nova.</summary>
public readonly record struct TimerChange(TimerChangeKind Kind, int TaskId, TimerOrigin Origin);
