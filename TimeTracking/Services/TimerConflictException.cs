namespace TimeTracking.Services;

/// <summary>O banco rejeitou a abertura de uma sessão pelo índice único da Seção 9 (já existe
/// uma TimeEntry aberta — um início concorrente). Traduz o DbUpdateException para um erro de
/// domínio, para que a UI mostre uma mensagem amigável (Seção 39) em vez do detalhe técnico,
/// que vai para o log (Seção 46).</summary>
public class TimerConflictException : Exception
{
    public TimerConflictException(Exception innerException)
        : base("Já existe um timer em execução. Atualize a tela e tente novamente.", innerException)
    {
    }
}
