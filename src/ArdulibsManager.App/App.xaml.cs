using System.Text;
using System.Windows;
using ArdulibsManager.Views;

namespace ArdulibsManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            MessageBox.Show(
                "Приложение не смогло запуститься. Подробности записаны в startup-error.log рядом с приложением." + Environment.NewLine + Environment.NewLine + ex.Message,
                "ArdulibsManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteStartupError(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(ex.ToString());
            File.WriteAllText(logPath, sb.ToString());
        }
        catch
        {
            // ignore logging errors
        }
    }
}
