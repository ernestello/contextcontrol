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

        if (options.Mode == SetupMode.Uninstall &&
            !options.UninstallRelaunched &&
            InstallerEngine.ShouldRelaunchUninstallFromTemp(options.InstallDir))
        {
            try
            {
                InstallerEngine.RelaunchUninstallerFromTemp(options);
                return 0;
            }
            catch (Exception ex)
            {
                InstallerEngine.WriteEmergencyLog(ex);
                MessageBox.Show(ex.Message, "ContextControl Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        if (options.Quiet)
        {
            try
            {
                if (options.Mode == SetupMode.Uninstall)
                {
                    InstallerEngine.Uninstall(options, ProgressSink.Null);
                }
                else
                {
                    InstallerEngine.Install(options, ProgressSink.Null);
                }

                return 0;
            }
            catch (Exception ex)
            {
                InstallerEngine.WriteEmergencyLog(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        using Form form = options.Mode == SetupMode.Uninstall
            ? new UninstallForm(options)
            : new SetupForm(options);
        Application.Run(form);
        return form is ISetupWindow setupWindow ? setupWindow.ExitCode : 1;
    }
}

internal interface ISetupWindow
{
    int ExitCode { get; }
}

internal sealed class SetupForm : Form, ISetupWindow
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
    private readonly SetupOptions _initialOptions;

    public SetupForm(SetupOptions initialOptions)
    {
        _initialOptions = initialOptions;
        Text = "ContextControl Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 540);
        MinimumSize = new Size(720, 500);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi(initialOptions);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ExitCode { get; private set; } = 1;

    private void BuildUi(SetupOptions initialOptions)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 3,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 6,
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Install ContextControl",
            Margin = new Padding(0, 0, 0, 8),
        };
        content.Controls.Add(title, 0, 0);

        var intro = new Label
        {
            AutoSize = true,
            Text = "Choose where the full app folder will be installed.",
            Margin = new Padding(0, 0, 0, 14),
        };
        content.Controls.Add(intro, 0, 1);

        var installLabel = new Label
        {
            AutoSize = true,
            Text = "Install folder",
            Margin = new Padding(0, 0, 0, 6),
        };
        content.Controls.Add(installLabel, 0, 2);

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
        content.Controls.Add(pathRow, 0, 3);

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

        content.Controls.Add(optionPanel, 0, 4);

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 110),
        };
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready to install.";
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        statusPanel.Controls.Add(_statusLabel);
        content.Controls.Add(statusPanel, 0, 5);
        shell.Controls.Add(content, 0, 0);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Height = 18;
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        shell.Controls.Add(_progressBar, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };

        _installButton.Text = "Install";
        _installButton.AutoSize = true;
        _installButton.Click += async (_, _) => await InstallAsync().ConfigureAwait(true);
        buttons.Controls.Add(_installButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        shell.Controls.Add(buttons, 0, 2);
        Controls.Add(shell);

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
            WaitForProcessId = _initialOptions.WaitForProcessId,
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

internal sealed class UninstallForm : Form, ISetupWindow
{
    private readonly string _installDir;
    private readonly CheckBox _removeUserDataBox = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _uninstallButton = new();
    private readonly Button _cancelButton = new();

    public UninstallForm(SetupOptions initialOptions)
    {
        _installDir = initialOptions.InstallDir;
        Text = "ContextControl Uninstall";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 360);
        MinimumSize = new Size(660, 330);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi(initialOptions);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ExitCode { get; private set; } = 1;

    private void BuildUi(SetupOptions initialOptions)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        shell.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Text = "Uninstall ContextControl",
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Remove the installed app folder and shortcuts.",
            Margin = new Padding(0, 0, 0, 14),
        }, 0, 1);

        var pathLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 44,
            Text = $"Install folder:{Environment.NewLine}{_installDir}",
            Margin = new Padding(0, 0, 0, 10),
        };
        shell.Controls.Add(pathLabel, 0, 2);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        middle.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _removeUserDataBox.AutoSize = true;
        _removeUserDataBox.Text = "Also remove ContextControl logs and user data";
        _removeUserDataBox.Checked = initialOptions.RemoveUserData;
        _removeUserDataBox.Margin = new Padding(0, 0, 0, 12);
        middle.Controls.Add(_removeUserDataBox, 0, 0);

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready to uninstall.";
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        middle.Controls.Add(_statusLabel, 0, 1);
        shell.Controls.Add(middle, 0, 3);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Height = 18;
        _progressBar.Margin = new Padding(0, 0, 0, 14);
        bottom.Controls.Add(_progressBar, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };

        _uninstallButton.Text = "Uninstall";
        _uninstallButton.AutoSize = true;
        _uninstallButton.Click += async (_, _) => await UninstallAsync().ConfigureAwait(true);
        buttons.Controls.Add(_uninstallButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        bottom.Controls.Add(buttons, 0, 1);
        shell.Controls.Add(bottom, 0, 4);
        Controls.Add(shell);

        AcceptButton = _uninstallButton;
        CancelButton = _cancelButton;
    }

    private async Task UninstallAsync()
    {
        SetUninstalling(true);
        var progress = new UiProgressSink(this);
        var options = new SetupOptions
        {
            Mode = SetupMode.Uninstall,
            InstallDir = _installDir,
            RemoveUserData = _removeUserDataBox.Checked,
            UninstallRelaunched = true,
        };

        try
        {
            var result = await Task.Run(() => InstallerEngine.Uninstall(options, progress)).ConfigureAwait(true);
            ExitCode = 0;
            SetStatus("ContextControl was uninstalled.");
            MessageBox.Show(this, $"ContextControl was uninstalled.{Environment.NewLine}{Environment.NewLine}Log:{Environment.NewLine}{result.LogPath}", "ContextControl Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            InstallerEngine.WriteEmergencyLog(ex);
            SetStatus("Uninstall failed. Details were written to the uninstall log.");
            MessageBox.Show(this, ex.Message, "ContextControl Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUninstalling(false);
        }
    }

    private void SetUninstalling(bool uninstalling)
    {
        _uninstallButton.Enabled = !uninstalling;
        _cancelButton.Enabled = !uninstalling;
        _removeUserDataBox.Enabled = !uninstalling;
        _progressBar.Style = uninstalling ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }

    internal void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private sealed class UiProgressSink(UninstallForm form) : IInstallProgress
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

internal enum SetupMode
{
    Install,
    Uninstall
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
    public SetupMode Mode { get; init; }
    public bool RemoveUserData { get; init; }
    public bool UninstallRelaunched { get; init; }
    public int? WaitForProcessId { get; init; }

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
                case "uninstall":
                case "remove":
                    options.Mode = SetupMode.Uninstall;
                    break;
                case "uninstallrelaunched":
                    options.UninstallRelaunched = true;
                    break;
                case "removeuserdata":
                    options.RemoveUserData = true;
                    break;
                case "waitforprocess":
                case "waitforpid":
                case "waitpid":
                    value ??= ReadNextValue(args, ref index, raw);
                    if (!int.TryParse(value, out var processId) || processId <= 0)
                    {
                        throw new ArgumentException($"Invalid process id for setup option: {raw}");
                    }

                    options.WaitForProcessId = processId;
                    break;
                case "installdir":
                case "installpath":
                case "dir":
                    value ??= ReadNextValue(args, ref index, raw);
                    options.InstallDirValue = value;
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

        if (options.Mode == SetupMode.Uninstall && !options.InstallDirSpecified)
        {
            options.InstallDir = InstallerEngine.ReadRegisteredInstallLocation() ?? DefaultInstallDir;
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
        public SetupMode Mode { get; set; } = SetupMode.Install;
        public bool RemoveUserData { get; set; }
        public bool UninstallRelaunched { get; set; }
        public int? WaitForProcessId { get; set; }
        public bool InstallDirSpecified { get; set; }

        public string InstallDirValue
        {
            get => InstallDir;
            set
            {
                InstallDir = value;
                InstallDirSpecified = true;
            }
        }

        public SetupOptions ToImmutable() =>
            new()
            {
                InstallDir = InstallDir,
                StartMenuShortcut = StartMenuShortcut,
                DesktopShortcut = DesktopShortcut,
                InstallWebView2Runtime = InstallWebView2Runtime,
                Launch = Launch,
                Quiet = Quiet,
                Mode = Mode,
                RemoveUserData = RemoveUserData,
                UninstallRelaunched = UninstallRelaunched,
                WaitForProcessId = WaitForProcessId,
            };
    }
}

internal static class InstallerEngine
{
    private const string ProductName = "ContextControl";
    private const string PayloadResourceName = "ContextControlPayload.zip";
    private const string UninstallerFileName = "ContextControl.Uninstall.exe";
    private const string UninstallRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ContextControl";

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

            WaitForProcessExit(options.WaitForProcessId, Log);
            StopRunningWorkbench(resolvedInstallDir, Log);

            Log("Extracting ContextControl app files...");
            ExtractPayload(resolvedInstallDir, Log);

            var exePath = Path.Combine(resolvedInstallDir, "ContextControl.Workbench.exe");
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("ContextControl.Workbench.exe was not found after extraction.", exePath);
            }

            Log("Preparing uninstaller...");
            var uninstallerPath = CopyUninstaller(resolvedInstallDir, Log);

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
                CreateShortcut(
                    Path.Combine(startMenuFolder, "Uninstall ContextControl.lnk"),
                    uninstallerPath,
                    resolvedInstallDir,
                    "/uninstall");
            }

            if (options.DesktopShortcut)
            {
                Log("Creating desktop shortcut...");
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContextControl.lnk"),
                    exePath,
                    resolvedInstallDir);
            }

            Log("Registering Windows uninstall entry...");
            RegisterUninstallEntry(resolvedInstallDir, exePath, uninstallerPath);

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

    public static UninstallResult Uninstall(SetupOptions options, IInstallProgress progress)
    {
        var log = new List<string>();
        var appDataLogPath = GetAppDataLogPath("uninstall.log");
        var resolvedInstallDir = PrepareInstallDir(options.InstallDir);

        void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            log.Add(line);
            progress.Report(message);
        }

        try
        {
            Log($"Preparing to remove {resolvedInstallDir}...");
            EnsureSafeUninstallTarget(resolvedInstallDir);

            Log("Stopping running ContextControl windows...");
            StopRunningWorkbench(resolvedInstallDir, Log);

            Log("Removing shortcuts...");
            RemoveShortcuts();

            Log("Removing Windows uninstall entry...");
            RemoveUninstallEntry();

            Log("Removing installed app folder...");
            Directory.Delete(resolvedInstallDir, recursive: true);

            if (options.RemoveUserData)
            {
                Log("Removing ContextControl user data...");
                RemoveUserData(Log);
            }

            Log("ContextControl uninstall finished.");
            var logPath = WriteLogs(log, null, appDataLogPath);
            return new UninstallResult(logPath);
        }
        catch (Exception ex)
        {
            log.Add($"{DateTimeOffset.Now:O} ERROR {ex}");
            WriteLogs(log, null, appDataLogPath);
            throw;
        }
    }

    public static string? ReadRegisteredInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistrySubKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool ShouldRelaunchUninstallFromTemp(string installDir)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(installDir))
        {
            return false;
        }

        try
        {
            var processFull = Path.GetFullPath(processPath);
            var installFull = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return processFull.StartsWith(installFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void RelaunchUninstallerFromTemp(SetupOptions options)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("The running setup executable could not be located for uninstall relaunch.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "ContextControl", "Uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, UninstallerFileName);
        File.Copy(processPath, tempExe, overwrite: true);

        var arguments = new List<string>
        {
            "/uninstall",
            "/uninstallRelaunched",
            $"/installDir={options.InstallDir}"
        };
        if (options.Quiet)
        {
            arguments.Add("/quiet");
        }

        if (options.RemoveUserData)
        {
            arguments.Add("/removeUserData");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tempExe,
            WorkingDirectory = tempDir,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
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

    private static string CopyUninstaller(string installDir, Action<string> log)
    {
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new InvalidOperationException("The setup executable could not be located, so the uninstaller could not be registered.");
        }

        var destination = Path.Combine(installDir, UninstallerFileName);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
            log($"Uninstaller copied to {destination}.");
        }

        return destination;
    }

    private static void RegisterUninstallEntry(string installDir, string exePath, string uninstallerPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistrySubKey, writable: true)
            ?? throw new InvalidOperationException("Could not create the Windows uninstall registry entry.");

        key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", Application.ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", "VulkanVX", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", exePath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" /uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" /uninstall /quiet", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstalledSizeKb(installDir), RegistryValueKind.DWord);
    }

    private static int EstimateInstalledSizeKb(string installDir)
    {
        try
        {
            var bytes = Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            var kb = Math.Max(1, bytes / 1024);
            return kb > int.MaxValue ? int.MaxValue : (int)kb;
        }
        catch
        {
            return 1;
        }
    }

    private static void RemoveUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistrySubKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // A missing or locked registry entry should not leave app files behind.
        }
    }

    private static void EnsureSafeUninstallTarget(string installDir)
    {
        var full = Path.GetFullPath(installDir);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(full) ||
            string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to uninstall unsafe folder: {full}");
        }

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Install folder was not found: {full}");
        }

        var workbenchExe = Path.Combine(full, "ContextControl.Workbench.exe");
        var uninstallerExe = Path.Combine(full, UninstallerFileName);
        if (!File.Exists(workbenchExe) && !File.Exists(uninstallerExe))
        {
            throw new InvalidOperationException($"The selected folder does not look like a ContextControl install: {full}");
        }
    }

    private static void StopRunningWorkbench(string installDir, Action<string> log)
    {
        var installFull = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName("ContextControl.Workbench"))
        {
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(path) ||
                    !Path.GetFullPath(path).StartsWith(installFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                log($"Stopping process {process.Id}...");
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    log($"Could not stop process {process.Id}: {ex.Message}");
                }
            }
        }
    }

    private static void WaitForProcessExit(int? processId, Action<string> log)
    {
        if (processId is null or <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (process.HasExited)
            {
                return;
            }

            log($"Waiting for ContextControl process {processId.Value} to exit...");
            if (!process.WaitForExit(30000))
            {
                log($"Process {processId.Value} is still running; setup will try to close it before updating files.");
            }
        }
        catch (ArgumentException)
        {
            // The launching process already exited.
        }
        catch (Exception ex)
        {
            log($"Could not wait for process {processId.Value}: {ex.Message}");
        }
    }

    private static void RemoveShortcuts()
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl.lnk"));
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl", "ContextControl.lnk"));
        DeleteFileIfExists(Path.Combine(startMenu, "Programs", "ContextControl", "Uninstall ContextControl.lnk"));
        DeleteDirectoryIfEmpty(Path.Combine(startMenu, "Programs", "ContextControl"));
        DeleteFileIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ContextControl.lnk"));
    }

    private static void RemoveUserData(Action<string> log)
    {
        DeleteDirectoryIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl"), log);
        DeleteDirectoryIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextControl"), log);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Shortcut cleanup is best-effort.
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Shortcut folder cleanup is best-effort.
        }
    }

    private static void DeleteDirectoryIfExists(string path, Action<string> log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                log($"Removed {path}.");
            }
        }
        catch (Exception ex)
        {
            log($"Could not remove {path}: {ex.Message}");
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

    private static string GetAppDataLogPath(string fileName = "install.log")
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextControl");
        return Path.Combine(root, fileName);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        CreateShortcut(shortcutPath, targetPath, workingDirectory, "");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments)
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
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [arguments]);
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, [string.IsNullOrWhiteSpace(arguments) ? "ContextControl Workbench" : "Uninstall ContextControl"]);
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
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
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

internal sealed record UninstallResult(string LogPath);

internal sealed record ProcessResult(int ExitCode, string Output, string Error);
