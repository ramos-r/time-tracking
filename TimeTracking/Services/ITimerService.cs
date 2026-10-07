using TimeTracking.Models;
using DomainTask = TimeTracking.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace TimeTracking.Services;

public interface ITimerService
{
    /// <summary>Calcula o estado do timer de uma tarefa a partir de suas TimeEntry.</summary>
    Task<TimerStatus> GetStatusAsync(int taskId);

    /// <summary>Retorna a tarefa com uma TimeEntry aberta no momento, se houver alguma
    /// (a regra é global — Seção 15 — no máximo uma tarefa ativa em toda a aplicação).</summary>
    Task<DomainTask?> GetActiveTaskAsync();

    /// <summary>Inicia o timer da tarefa. Se outra tarefa estiver em execução, ela é
    /// pausada automaticamente (a confirmação da Seção 15 é responsabilidade da UI/ViewModel,
    /// que deve perguntar antes de chamar este método quando houver conflito).</summary>
    /// <exception cref="TimerConflictException">O banco rejeitou a abertura pelo índice único
    /// da Seção 9 (início concorrente).</exception>
    Task StartAsync(int taskId, TimerOrigin origin = TimerOrigin.User);

    /// <summary>Encerra a sessão aberta da tarefa (Seção 12).</summary>
    Task PauseAsync(int taskId, TimerOrigin origin = TimerOrigin.User);

    /// <summary>Mecanicamente idêntico a Pause (Seção 14, item 15 da nota de revisão) —
    /// mantido como método separado por clareza semântica de UX.</summary>
    Task StopAsync(int taskId, TimerOrigin origin = TimerOrigin.User);

    /// <summary>Encerra a sessão aberta da tarefa em um horário específico, em vez de "agora"
    /// (Seção 34, v1.5.1) — base da regra crítica do Pomodoro (Seção 70): o foco termina no fim
    /// teórico, nunca em UtcNow. Não faz nada se a tarefa não tem sessão aberta.</summary>
    /// <exception cref="ArgumentException">endedAtUtc anterior ao início da sessão ou posterior
    /// a "agora".</exception>
    Task StopAtAsync(int taskId, DateTime endedAtUtc, TimerOrigin origin = TimerOrigin.User);

    /// <summary>Retorna as TimeEntry de uma tarefa, ordenadas por início — usadas pelo
    /// painel de edição (Seção 17) para decidir entre edição direta ou modo agregado.</summary>
    Task<List<TimeEntry>> GetEntriesForTaskAsync(int taskId);

    /// <summary>Atualiza os timestamps de uma TimeEntry específica (Seção 17: só permitido
    /// quando a tarefa possui exatamente uma sessão). Não altera TaskId.</summary>
    Task UpdateEntryTimestampsAsync(int entryId, DateTime startedAt, DateTime? endedAt);

    /// <summary>Cria uma sessão já encerrada manualmente, sem passar pelo fluxo Start/Pause
    /// (criação retroativa via "Nova tarefa"). Se endedAtUtc == startedAtUtc, a sessão fica
    /// registrada com duração zero e a tarefa entra em estado "pausada" — nunca em execução —
    /// até o usuário dar Play para continuar.</summary>
    Task AddManualEntryAsync(int taskId, DateTime startedAtUtc, DateTime endedAtUtc);

    /// <summary>Disparado sempre que uma sessão é aberta ou encerrada, ou seja, quando a
    /// existência de uma tarefa ativa (Seção 15) pode ter mudado, informando o tipo, a tarefa e
    /// a origem. Substitui o antigo ActiveTaskChanged (v1.5.1). Consumidores: a MainWindow, para
    /// trocar o ícone da barra de tarefas (Seção 71) sem polling, e o PomodoroService (Seção 70).
    /// Uma troca de tarefa dispara dois eventos: Ended da anterior e Started da nova.</summary>
    event Action<TimerChange>? TimerChanged;
}
