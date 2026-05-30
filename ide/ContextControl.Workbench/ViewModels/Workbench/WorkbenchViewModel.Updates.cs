// CC-DESC: GitHub release update check and installer handoff for the Workbench shell.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
            {
                CheckForUpdatesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (SetProperty(ref _isDownloadingUpdate, value))
            {
                CheckForUpdatesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UpdateButtonLabel
    {
        get => _updateButtonLabel;
        private set => SetProperty(ref _updateButtonLabel, value);
    }

    public string UpdateStatusLabel
    {
        get => _updateStatusLabel;
        private set => SetProperty(ref _updateStatusLabel, value);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await CheckForUpdatesAsync(silent: true);
        }
        catch
        {
            // Startup update checks are best-effort and must never block the workbench.
        }
    }

    private async Task CheckForUpdatesOrInstallAsync()
    {
        if (_availableUpdate is { IsAvailable: true } update)
        {
            await DownloadAndLaunchUpdateAsync(update);
            return;
        }

        await CheckForUpdatesAsync(silent: false);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        IsCheckingForUpdates = true;
        if (!silent)
        {
            UpdateButtonLabel = "Checking";
            UpdateStatusLabel = "Checking GitHub releases...";
        }

        try
        {
            var update = await _updateService.CheckLatestAsync();
            _availableUpdate = update.IsAvailable ? update : null;
            if (update.IsAvailable)
            {
                UpdateButtonLabel = "Install update";
                UpdateStatusLabel = $"ContextControl {update.LatestVersion} is available. Current version: {update.CurrentVersion}.";
            }
            else
            {
                UpdateButtonLabel = "Check updates";
                UpdateStatusLabel = $"ContextControl {update.CurrentVersion} is up to date.";
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                UpdateStatusLabel = $"Update check failed: {ex.Message}";
            }

            UpdateButtonLabel = "Check updates";
            _availableUpdate = null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task DownloadAndLaunchUpdateAsync(AppUpdateInfo update)
    {
        IsDownloadingUpdate = true;
        UpdateButtonLabel = "Downloading";
        UpdateStatusLabel = $"Downloading ContextControl {update.LatestVersion}...";
        try
        {
            var progress = new Progress<string>(status => UpdateStatusLabel = status);
            var download = await _updateService.DownloadInstallerAsync(update, progress);
            if (!download.Succeeded || string.IsNullOrWhiteSpace(download.InstallerPath))
            {
                UpdateStatusLabel = download.Status;
                UpdateButtonLabel = "Check updates";
                _availableUpdate = null;
                return;
            }

            UpdateButtonLabel = "Starting";
            UpdateStatusLabel = "Starting the ContextControl installer. The installer may close this running app before updating files.";
            var launch = _updateService.LaunchInstaller(download.InstallerPath, AppContext.BaseDirectory);
            UpdateStatusLabel = launch.Status;
            if (!launch.Succeeded)
            {
                UpdateButtonLabel = "Install update";
                return;
            }

            UpdateButtonLabel = "Installer open";
        }
        catch (Exception ex)
        {
            UpdateStatusLabel = $"Update install failed: {ex.Message}";
            UpdateButtonLabel = "Install update";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }
}
