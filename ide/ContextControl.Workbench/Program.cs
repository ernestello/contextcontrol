using Avalonia;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ContextControl.Workbench;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                StartupFailureReporter.Report(exception, showMessage: true);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupFailureReporter.Report(e.Exception, showMessage: false);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            StartupFailureReporter.Report(ex, showMessage: true);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}

internal static class StartupFailureReporter
{
    private static int _messageShown;

    public static void Report(Exception exception, bool showMessage)
    {
        var logPath = WriteCrashLog(exception);
        Trace.WriteLine(exception.ToString());

        if (!showMessage || Interlocked.Exchange(ref _messageShown, 1) != 0)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine("ContextControl failed to start.")
            .AppendLine()
            .AppendLine(exception.Message)
            .AppendLine()
            .AppendLine("Crash log:")
            .AppendLine(logPath)
            .ToString();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            MessageBoxW(IntPtr.Zero, message, "ContextControl", 0x00000010);
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

    private static string WriteCrashLog(Exception exception)
    {
        var logText = $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}";
        var appDataLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextControl",
            "workbench-crash.log");

        TryWriteLog(appDataLogPath, logText);

        var localLogPath = Path.Combine(AppContext.BaseDirectory, "ContextControl.Workbench.crash.log");
        return TryWriteLog(localLogPath, logText) ? localLogPath : appDataLogPath;
    }

    private static bool TryWriteLog(string path, string text)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.AppendAllText(path, text, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
