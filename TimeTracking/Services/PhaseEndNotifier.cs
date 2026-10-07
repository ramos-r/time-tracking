using TimeTracking.Helpers;

namespace TimeTracking.Services;

/// <summary>
/// Aviso de fim de fase (Seção 70): componente pequeno e isolado, ligado ao evento
/// IPomodoroService.PhaseEnded — o serviço não conhece som nem janelas. O evento só dispara
/// quando o fim é detectado com o app aberto (tick, ação do usuário ou volta de suspensão); na
/// recuperação ao reabrir o app ele não dispara, então reabrir depois do fim não toca nada.
/// O estado "Foco concluído — iniciar pausa?" aparece na tela Pomodoro e no chip da barra
/// superior pelo próprio estado do serviço (Fase 12B), independente deste componente.
/// </summary>
public class PhaseEndNotifier
{
    private readonly INativeAlerts _alerts;

    public PhaseEndNotifier(IPomodoroService pomodoroService, INativeAlerts alerts)
    {
        _alerts = alerts;
        pomodoroService.PhaseEnded += OnPhaseEnded;
    }

    private void OnPhaseEnded(PomodoroPhase endedPhase)
    {
        var isFocusEnd = endedPhase == PomodoroPhase.Focus;

        // Cada aviso é isolado: se o som falhar (sem dispositivo de áudio, por exemplo), a barra
        // de tarefas ainda pisca — e nenhuma falha aqui pode chegar ao serviço, que dispara o
        // evento fora do semáforo mas dentro da chamada do tick.
        Try(() => _alerts.PlaySound(isFocusEnd), "som");
        Try(_alerts.FlashTaskbar, "barra de tarefas");
    }

    private static void Try(Action alert, string description)
    {
        try
        {
            alert();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Falha no aviso de fim de fase ({description}).", ex);
        }
    }
}
