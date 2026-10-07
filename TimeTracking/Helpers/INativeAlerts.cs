namespace TimeTracking.Helpers;

/// <summary>
/// Os dois avisos nativos do fim de fase (Seção 70): som do sistema e piscar o botão na barra de
/// tarefas. Fica atrás de uma interface para o PhaseEndNotifier — a regra de quando avisar —
/// poder ser testado sem tocar som nem mexer em janelas de verdade.
/// </summary>
public interface INativeAlerts
{
    /// <summary>Toca um som do sistema. <paramref name="isFocusEnd"/>: fim de foco ("hora de
    /// descansar") ou fim de pausa ("hora de voltar") — sons diferentes.</summary>
    void PlaySound(bool isFocusEnd);

    /// <summary>Pisca o botão da janela principal na barra de tarefas até ela ser ativada. Nunca
    /// traz a janela para frente nem rouba o foco de outro aplicativo.</summary>
    void FlashTaskbar();
}
