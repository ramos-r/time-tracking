using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TimeTracking.Helpers;

/// <summary>Implementação Windows dos avisos de fim de fase (Seção 70): SystemSounds e
/// FlashWindowEx via P/Invoke, sem dependência externa (Regra 5).</summary>
public class WindowsAlerts : INativeAlerts
{
    // FLASHW_TRAY: pisca só o botão da barra de tarefas. FLASHW_TIMERNOFG: continua piscando até a
    // janela ir para o primeiro plano — ou seja, até o usuário ativá-la. Funciona com a janela
    // minimizada e, ao contrário de Activate()/Topmost, não rouba o foco de ninguém.
    private const uint FlashTray = 0x00000002;
    private const uint FlashUntilForeground = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public void PlaySound(bool isFocusEnd)
    {
        // Asterisk (informação) ao fim do foco, Exclamation (atenção) ao fim da pausa.
        (isFocusEnd ? SystemSounds.Asterisk : SystemSounds.Exclamation).Play();
    }

    public void FlashTaskbar()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        void Flash()
        {
            if (application.MainWindow is not { } window)
            {
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Window = handle,
                Flags = FlashTray | FlashUntilForeground,
                Count = uint.MaxValue,
                Timeout = 0,
            };
            FlashWindowEx(ref info);
        }

        if (application.Dispatcher.CheckAccess())
        {
            Flash();
        }
        else
        {
            application.Dispatcher.Invoke(Flash);
        }
    }
}
