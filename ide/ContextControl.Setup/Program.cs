using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ContextControl.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetupOptions options;
        try
        {
            options = SetupOptions.Parse(args);
        }
        catch (Exception ex)
        {
            InstallerEngine.WriteEmergencyLog(ex);
            MessageBox.Show(ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        if (options.Quiet)
        {
            try
            {
                InstallerEngine.Install(options, ProgressSink.Null);
                return 0;
            }
            catch (Exception ex)
            {
                InstallerEngine.WriteEmergencyLog(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        using var form = new SetupForm(options);
        Application.Run(form);
        return form.ExitCode;
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox _installDirBox = new();
    private readonly CheckBox _startMenuShortcutBox = new();
    private readonly CheckBox _desktopShortcutBox = new();
    private readonly CheckBox _installWebView2RuntimeBox = new();
    private readonly CheckBox _launchBox = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _installButton = new();
    private readonly Button _cancelButton = new();

    public SetupForm(SetupOptions initialOptions)
    {
        Text = "ContextControl Setup";
        ClientSize = new Size(640, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi(initialOptions);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ExitCode { get; private set; } = 1;

    private void BuildUi(SetupOptions initialOptions)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 8,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Install ContextControl",
            Margin = new Padding(0, 0, 0, 8),
        };
        root.Controls.Add(title, 0, 0);

        var intro = new Label
        {
            AutoSize = true,
            Text = "Choose where the full app folder will be installed.",
            Margin = new Padding(0, 0, 0, 14),
        };
        root.Controls.Add(intro, 0, 1);

        var installLabel = new Label
        {
            AutoSize = true,
            Text = "Install folder",
            Margin = new Padding(0, 0, 0, 6),
        };
        root.Controls.Add(installLabel, 0, 2);

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            AutoSize = true,
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _installDirBox.Dock = DockStyle.Top;
        _installDirBox.Text = initialOptions.InstallDir;
        _installDirBox.Margin = new Padding(0, 0, 8, 0);
        pathRow.Controls.Add(_installDirBox, 0, 0);

        var browseButton = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            Margin = new Padding(0),
        };
        browseButton.Click += OnBrowse;
        pathRow.Controls.Add(browseButton, 1, 0);
        root.Controls.Add(pathRow, 0, 3);

        var optionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _startMenuShortcutBox.AutoSize = true;
        _startMenuShortcutBox.Text = "Create Start Menu shortcut";
        _startMenuShortcutBox.Checked = initialOptions.StartMenuShortcut;
        optionPanel.Controls.Add(_startMenuShortcutBox);

        _desktopShortcutBox.AutoSize = true;
        _desktopShortcutBox.Text = "Create desktop shortcut";
        _desktopShortcutBox.Checked = initialOptions.DesktopShortcut;
        optionPanel.Controls.Add(_desktopShortcutBox);

        _installWebView2RuntimeBox.AutoSize = true;
        _installWebView2RuntimeBox.Text = "Install or repair Microsoft Edge WebView2 Runtime for the Browser workspace";
        _installWebView2RuntimeBox.Checked = initialOptions.InstallWebView2Runtime;
        optionPanel.Controls.Add(_installWebView2RuntimeBox);

        _launchBox.AutoSize = true;
        _launchBox.Text = "Launch ContextControl when setup finishes";
        _launchBox.Checked = initialOptions.Launch;
        optionPanel.Controls.Add(_launchBox);

        root.Controls.Add(optionPanel, 0, 4);

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 86),
        };
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready to install.";
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        statusPanel.Controls.Add(_statusLabel);
        root.Controls.Add(statusPanel, 0, 5);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Height = 18;
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        root.Controls.Add(_progressBar, 0, 6);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };

        _installButton.Text = "Install";
        _installButton.AutoSize = true;
        _installButton.Click += async (_, _) => await InstallAsync().ConfigureAwait(true);
        buttons.Controls.Add(_installButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        root.Controls.Add(buttons, 0, 7);
        Controls.Add(root);

        AcceptButton = _installButton;
        CancelButton = _cancelButton;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the ContextControl install folder",
            UseDescriptionForTitle = true,
            SelectedPath = Environment.ExpandEnvironmentVariables(_installDirBox.Text),
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installDirBox.Text = dialog.SelectedPath;
        }
    }

    private async Task InstallAsync()
    {
        SetInstalling(true);
        var options = CaptureOptions();
        var progress = new UiProgressSink(this);

        try
        {
            var result = await Task.Run(() => InstallerEngine.Install(options, progress)).ConfigureAwait(true);
            ExitCode = 0;
            SetStatus($"Installed to {result.InstallDir}");

            var message = $"ContextControl was installed to:{Environment.NewLine}{result.InstallDir}";
            var icon = MessageBoxIcon.Information;
            if (!string.IsNullOrWhiteSpace(result.LaunchError))
            {
                message += $"{Environment.NewLine}{Environment.NewLine}Setup could not keep the app running after launch:{Environment.NewLine}{result.LaunchError}{Environment.NewLine}{Environment.NewLine}Install log:{Environment.NewLine}{result.LogPath}";
                icon = MessageBoxIcon.Warning;
            }
            else
            {
                message += $"{Environment.NewLine}{Environment.NewLine}Install log:{Environment.NewLine}{result.LogPath}";
            }

            MessageBox.Show(this, message, "ContextControl Setup", MessageBoxButtons.OK, icon);
            Close();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            InstallerEngine.WriteEmergencyLog(ex);
            SetStatus("Install failed. Details were written to the install log.");
            MessageBox.Show(this, ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetInstalling(false);
        }
    }

    private SetupOptions CaptureOptions()
    {
        var installDir = _installDirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            installDir = SetupOptions.DefaultInstallDir;
        }

        return new SetupOptions
        {
            InstallDir = installDir,
            StartMenuShortcut = _startMenuShortcutBox.Checked,
            DesktopShortcut = _desktopShortcutBox.Checked,
            InstallWebView2Runtime = _installWebView2RuntimeBox.Checked,
            Launch = _launchBox.Checked,
        };
    }

    private void SetInstalling(bool installing)
    {
        _installButton.Enabled = !installing;
        _cancelButton.Enabled = !installing;
        _installDirBox.Enabled = !installing;
        _startMenuShortcutBox.Enabled = !installing;
        _desktopShortcutBox.Enabled = !installing;
        _installWebView2RuntimeBox.Enabled = !installing;
        _launchBox.Enabled = !installing;
        _progressBar.Style = installing ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }

    internal void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private sealed class UiProgressSink(SetupForm form) : IInstallProgress
    {
        public void Report(string message)
        {
            if (form.IsDisposed)
            {
                return;
            }

            if (form.InvokeRequired)
            {
                form.BeginInvoke((Action)(() => form.SetStatus(message)));
            }
            else
            {
                form.SetStatus(message);
            }
        }
    }
}

internal sealed class SetupOptions
{
    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ContextControl");

    public string InstallDir { get; init; } = DefaultInstallDir;
    public bool StartMenuShortcut { get; init; } = true;
    public bool DesktopShortcut { get; init; }
    public bool InstallWebView2Runtime { get; init; }
    public bool Launch { get; init; } = true;
    public bool Quiet { get; init; }

    public static SetupOptions Parse(string[] args)
    {
        var options = new MutableSetupOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index].Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var separatorIndex = raw.IndexOf('=');
            var key = separatorIndex >= 0 ? raw[..separatorIndex] : raw;
            var value = separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : null;
            key = key.TrimStart('-', '/').Trim();

            switch (key.ToLowerInvariant())
            {
                case "quiet":
                case "silent":
                    options.Quiet = true;
                    break;
                case "installdir":
                case "installpath":
                case "dir":
                    value ??= ReadNextValue(args, ref index, raw);
                    options.InstallDir = value;
                    break;
                case "startmenushortcut":
                case "startmenu":
                    options.StartMenuShortcut = true;
                    break;
                case "nostartmenushortcut":
                case "nostartmenu":
                    options.StartMenuShortcut = false;
                    break;
                case "desktopshortcut":
                case "desktop":
                    options.DesktopShortcut = true;
                    break;
                case "nodesktopshortcut":
                case "nodesktop":
                    options.DesktopShortcut = false;
                    break;
                case "installwebview2runtime":
                case "installwebview2":
                case "webview2":
                    options.InstallWebView2Runtime = true;
                    break;
                case "launch":
                    options.Launch = true;
                    break;
                case "nolaunch":
                    options.Launch = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown setup option: {raw}");
            }
        }

        return options.ToImmutable();
    }

    private static string ReadNextValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for setup option: {optionName}");
        }

        index++;
        return args[index];
    }

    private sealed class MutableSetupOptions
    {
        public string InstallDir { get; set; } = DefaultInstallDir;
        public bool StartMenuShortcut { get; set; } = true;
        public bool DesktopShortcut { get; set; }
        public bool InstallWebView2Runtime { get; set; }
        public bool Launch { get; set; } = true;
        public bool Quiet { get; set; }

        public SetupOptions ToImmutable() =>
            new()
            {
                InstallDir = InstallDir,
                StartMenuShortcut = StartMenuShortcut,
                DesktopShortcut = DesktopShortcut,
                InstallWebView2Runtime = InstallWebView2Runtime,
                Launch = Launch,
                Quiet = Quiet,
            };
    }
}

internal static class InstallerEngine
{
    private const string PayloadResourceName = "ContextControlPayload.zip";

    public static InstallResult Install(SetupOptions options, IInstallProgress progress)
    {
        var log = new List<string>();
        string? resolvedInstallDir = null;
        var appDataLogPath = GetAppDataLogPath();

        void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            log.Add(line);
            progress.Report(message);
        }

        try
        {
            Log("Preparing install folder...");
            resolvedInstallDir = PrepareInstallDir(options.InstallDir);
            Directory.CreateDirectory(resolvedInstallDir);

            Log("Extracting ContextControl app files...");
            ExtractPayload(resolvedInstallDir, Log);

            var exePath = Path.Combine(resolvedInstallDir, "ContextControl.Workbench.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("ContextControl.Workbench.exe was not found after extraction.", exePath);
            }

            if (options.InstallWebView2Runtime)
            {
                InstallWebView2Runtime(Log);
            }
            else if (!TestWebView2Runtime())
            {
                Log("WebView2 Runtime was not detected. The app will still open, but the Browser workspace may need WebView2 later.");
            }

            if (options.StartMenuShortcut)
            {
                Log("Creating Start Menu shortcut...");
                var startMenuFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    "ContextControl");
                CreateShortcut(
                    Path.Combine(startMenuFolder, "ContextControl.lnk"),
                    exePath,
                    resolvedInstallDir);
            }

            if (options.DesktopShortcut)
            {
                Log("Creating desktop shortcut...");
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContextControl.lnk"),
                    exePath,
                    resolvedInstallDir);
            }

            string? launchError = null;
            if (options.Launch)
            {
                Log("Launching ContextControl...");
                launchError = LaunchApp(exePath, resolvedInstallDir);
                if (!string.IsNullOrWhiteSpace(launchError))
                {
                    Log($"Launch warning: {launchError}");
                }
            }

            Log("ContextControl install finished.");
            var installLogPath = WriteLogs(log, resolvedInstallDir, appDataLogPath);
            return new InstallResult(resolvedInstallDir, exePath, installLogPath, launchError);
        }
        catch (Exception ex)
        {
            log.Add($"{DateTimeOffset.Now:O} ERROR {ex}");
            WriteLogs(log, resolvedInstallDir, appDataLogPath);
            throw;
        }
    }

    public static void WriteEmergencyLog(Exception exception)
    {
        try
        {
            var logPath = GetAppDataLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"{DateTimeOffset.Now:O} ERROR {exception}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Last-resort logging must not hide the original setup failure.
        }
    }

    private static string PrepareInstallDir(string installDir)
    {
        var expanded = Environment.ExpandEnvironmentVariables(installDir.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            expanded = SetupOptions.DefaultInstallDir;
        }

        return Path.GetFullPath(expanded);
    }

    private static void ExtractPayload(string installDir, Action<string> log)
    {
        using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        if (payloadStream is null)
        {
            var resources = string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames());
            throw new InvalidOperationException($"Installer payload is missing. Embedded resources: {resources}");
        }

        using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var entries = archive.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            if (index % 25 == 0)
            {
                log($"Extracting files... {index + 1}/{entries.Count}");
            }

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!destinationPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Installer payload contains an unsafe path: {entry.FullName}");
            }

            if (relativePath.EndsWith(Path.DirectorySeparatorChar))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var fileName = Path.GetFileName(destinationPath);
            if (string.Equals(fileName, ".ccWorkbench.settings.json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(destinationPath))
            {
                log("Keeping existing .ccWorkbench.settings.json.");
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string WriteLogs(IReadOnlyCollection<string> lines, string? installDir, string appDataLogPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appDataLogPath)!);
        File.WriteAllLines(appDataLogPath, lines, System.Text.Encoding.UTF8);

        if (string.IsNullOrWhiteSpace(installDir))
        {
            return appDataLogPath;
        }

        try
        {
            var installLogPath = Path.Combine(installDir, "install.log");
            File.WriteAllLines(installLogPath, lines, System.Text.Encoding.UTF8);
            return installLogPath;
        }
        catch
        {
            return appDataLogPath;
        }
    }

    private static string GetAppDataLogPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextControl");
        return Path.Combine(root, "install.log");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var parent = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
            throw new InvalidOperationException("WScript.Shell is unavailable, so setup could not create a shortcut.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            var shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory]);
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["ContextControl Workbench"]);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? LaunchApp(string exePath, string workingDirectory)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });

            if (process is not null && process.WaitForExit(2000))
            {
                return $"ContextControl exited immediately with code {process.ExitCode}. Try running {exePath} directly, or check install.log.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static bool TestWebView2Runtime()
    {
        return TestWebView2Runtime(RegistryHive.LocalMachine, RegistryView.Registry64) ||
            TestWebView2Runtime(RegistryHive.LocalMachine, RegistryView.Registry32) ||
            TestWebView2Runtime(RegistryHive.CurrentUser, RegistryView.Default);
    }

    private static bool TestWebView2Runtime(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var clients = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients");
            if (clients is null)
            {
                return false;
            }

            foreach (var subKeyName in clients.GetSubKeyNames())
            {
                using var client = clients.OpenSubKey(subKeyName);
                var name = client?.GetValue("name") as string;
                if (name?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void InstallWebView2Runtime(Action<string> log)
    {
        if (TestWebView2Runtime())
        {
            log("WebView2 Runtime is already installed.");
            return;
        }

        log("Installing Microsoft Edge WebView2 Runtime with winget...");
        ProcessResult result;
        try
        {
            result = RunProcess(
                "winget",
                "install --id Microsoft.EdgeWebView2Runtime --exact --silent --accept-package-agreements --accept-source-agreements",
                TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            log($"WebView2 Runtime install was skipped because winget could not be started: {ex.Message}");
            return;
        }

        if (result.ExitCode == 0)
        {
            log("WebView2 Runtime installation finished.");
            return;
        }

        log($"WebView2 Runtime install was skipped or failed. winget exit code: {result.ExitCode}. {result.Output} {result.Error}");
    }

    private static ProcessResult RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var timeoutMilliseconds = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort after a timed-out optional dependency install.
            }

            return new ProcessResult(-1, outputTask.GetAwaiter().GetResult(), "Timed out while running winget. " + errorTask.GetAwaiter().GetResult());
        }

        return new ProcessResult(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }
}

internal interface IInstallProgress
{
    void Report(string message);
}

internal sealed class ProgressSink : IInstallProgress
{
    public static readonly ProgressSink Null = new();

    private ProgressSink()
    {
    }

    public void Report(string message)
    {
    }
}

internal sealed record InstallResult(string InstallDir, string ExePath, string LogPath, string? LaunchError);

internal sealed record ProcessResult(int ExitCode, string Output, string Error);
