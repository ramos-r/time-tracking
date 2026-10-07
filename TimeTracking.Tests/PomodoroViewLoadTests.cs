using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Data;
using TimeTracking.Repositories;
using TimeTracking.Services;
using TimeTracking.ViewModels;
using TimeTracking.Views;
using TimeTracking.Views.Components;

namespace TimeTracking.Tests;

/// <summary>
/// Teste de fumaça da tela: carrega a PomodoroView de verdade (XAML + estilos + tema Dark) numa
/// thread STA e força o layout em cada fase. Pega o que os testes de ViewModel não pegam —
/// recurso estático ausente, binding com tipo errado, template quebrado — sem precisar abrir o app.
/// </summary>
public class PomodoroViewLoadTests
{
    [Fact]
    public void PomodoroView_LoadsAndLaysOut_InEveryPhase()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunSmokeTest();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "A thread de UI não terminou.");

        Assert.True(failure is null, failure?.ToString());
    }

    /// <summary>Deixa o dispatcher da thread STA processar layout, render e animações por um tempo
    /// real (as animações do WPF andam com o relógio, não com o TestClock).</summary>
    private static void Pump(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    private static void RunSmokeTest()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var path in new[]
                 {
                     "Resources/Themes/Dark.xaml", "Resources/Icons/Icons.xaml", "Resources/Styles/Buttons.xaml",
                     "Resources/Styles/Inputs.xaml", "Resources/Styles/Cards.xaml", "Resources/Styles/Menus.xaml",
                     "Resources/Styles/ColorPicker.xaml",
                 })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/TimeTracking;component/{path}", UriKind.Absolute),
            });
        }

        // Cores de destaque são publicadas em runtime pelo AccentColorService; aqui, valores fixos.
        foreach (var key in new[] { "PrimaryBrush", "PrimaryHoverBrush", "PrimaryPressedBrush", "PrimarySubtleBrush", "TextOnPrimaryBrush" })
        {
            app.Resources[key] = new SolidColorBrush(Color.FromRgb(0x71, 0x29, 0xD3));
        }

        app.Resources["AppFontFamily"] = new FontFamily("Segoe UI");
        app.Resources["AppFontFamilyLight"] = new FontFamily("Segoe UI");

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var context = new AppDbContext(options))
        {
            context.Database.Migrate();
        }

        var factory = new TestDbContextFactory(options);
        var clock = new TestClock();
        var taskService = new TaskService(new TaskRepository(factory));
        var timer = new TimerService(new TimeEntryRepository(factory), new TaskRepository(factory), clock);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"tt_view_smoke_{Guid.NewGuid():N}.json");
        try
        {
            var service = new PomodoroService(timer, taskService, new AppSettingsStore(settingsPath), clock);
            var viewModel = new PomodoroViewModel(service, taskService);
            var view = new PomodoroView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 900, Height = 700 };

            void Layout()
            {
                view.Measure(new Size(900, 700));
                view.Arrange(new Rect(0, 0, 900, 700));
                view.UpdateLayout();
                Assert.True(view.ActualWidth > 0 && view.ActualHeight > 0);
            }

            Layout(); // Idle
            viewModel.PrimaryActionCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Layout(); // Foco
            clock.UtcNow += TimeSpan.FromMinutes(25);
            viewModel.TickAsync().GetAwaiter().GetResult();
            Layout(); // AguardandoPausa
            viewModel.PrimaryActionCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Layout(); // Pausa curta
            viewModel.PrimaryActionCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Layout(); // Pausa pausada
            viewModel.StopCycleCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Layout(); // Idle de novo

            // "Aba" de configurações: recolhida por padrão, abre ao alternar.
            Assert.False(viewModel.IsSettingsOpen);
            viewModel.ToggleSettingsCommand.Execute(null);
            Layout();
            var heightWithSettings = view.ActualHeight;
            Assert.True(heightWithSettings > 0);

            // Botões − e + do campo numérico: passam pelo mesmo texto que o ViewModel valida.
            var stepper = new NumberStepper { Minimum = 1, Maximum = 3, Text = "2" };
            var increment = (System.Windows.Controls.Primitives.RepeatButton)stepper.FindName("IncrementButton");
            var decrement = (System.Windows.Controls.Primitives.RepeatButton)stepper.FindName("DecrementButton");
            increment.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("3", stepper.Text);
            Assert.False(increment.IsEnabled); // no máximo
            increment.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("3", stepper.Text);
            decrement.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            decrement.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("1", stepper.Text);
            Assert.False(decrement.IsEnabled); // no mínimo
            Assert.True(increment.IsEnabled);

            // Janela de verdade (fora da tela): exercita o code-behind — centralização do bloco,
            // aba de configurações animada e a garantia de que abrir/fechar a aba NÃO move o que
            // está acima dela.
            // Janela alta o bastante para a aba caber sem rolagem: só nesse caso "nada acima se
            // move" é verificável (numa janela baixa a aba abre rolando a tela, de propósito).
            window.Height = 1000;
            viewModel.IsSettingsOpen = false;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            Pump(TimeSpan.FromMilliseconds(300));
            Assert.True(view.IsLoaded);

            var mainBlock = (FrameworkElement)view.FindName("MainBlock");
            var scroll = (FrameworkElement)view.FindName("Scroll");
            var settingsHost = (FrameworkElement)view.FindName("SettingsHost");
            double MainTop() => mainBlock.TranslatePoint(new Point(0, 0), view).Y;

            Assert.Equal(Visibility.Collapsed, settingsHost.Visibility);
            var closedTop = MainTop();
            var expectedTop = Math.Max(16, (scroll.ActualHeight - mainBlock.ActualHeight) / 2);
            var rootStack = (FrameworkElement)view.FindName("RootStack");
            Assert.True(
                Math.Abs(closedTop - expectedTop) <= 2, // centralizado na vertical
                $"top={closedTop} esperado={expectedTop} scroll={scroll.ActualHeight} bloco={mainBlock.ActualHeight} margem={rootStack.Margin} raiz={rootStack.ActualHeight}");
            var centerX = mainBlock.TranslatePoint(new Point(mainBlock.ActualWidth / 2, 0), view).X;
            Assert.InRange(centerX, view.ActualWidth / 2 - 2, view.ActualWidth / 2 + 2); // e na horizontal

            viewModel.ToggleSettingsCommand.Execute(null);
            Pump(TimeSpan.FromMilliseconds(600));
            Assert.Equal(Visibility.Visible, settingsHost.Visibility);
            Assert.True(settingsHost.ActualHeight > 50, "A aba deveria ter aberto.");
            Assert.InRange(MainTop(), closedTop - 1, closedTop + 1); // nada acima se moveu

            viewModel.ToggleSettingsCommand.Execute(null);
            Pump(TimeSpan.FromMilliseconds(600));
            Assert.Equal(Visibility.Collapsed, settingsHost.Visibility);
            Assert.InRange(MainTop(), closedTop - 1, closedTop + 1);

            // Piscar a barra de tarefas (12C) com a janela real: a chamada nativa não pode lançar
            // (o efeito visível — o botão piscando — só dá para conferir à mão).
            app.MainWindow = window;
            new TimeTracking.Helpers.WindowsAlerts().FlashTaskbar();

            window.Close();
            window.Content = null;
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }

            app.Shutdown();
        }
    }
}
