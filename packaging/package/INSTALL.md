# ContextControl Portable Install

This package is a self-contained Windows x64 build. It does not require the .NET runtime to be installed.

## Portable

Run `ContextControl.Workbench.exe` from this folder.

This is a full self-contained app folder, not a single-file launcher. Keep the files together.

The bundled `.ccWorkbench.settings.json` opens the app on the Local LLM catalog with the release appearance defaults. Your later settings, chat history, downloaded dependencies, and model files are kept outside the GitHub source tree.

## User-Local Install

Run `ContextControl-win-x64-Setup.exe` if you downloaded the installer EXE from GitHub Releases.

The setup EXE lets you choose the install folder, create Start Menu or desktop shortcuts, optionally install Microsoft Edge WebView2 Runtime, and launch ContextControl when setup finishes. It writes `install.log` in the installed folder and in `%LOCALAPPDATA%\ContextControl\install.log`.

For the portable zip, run this in PowerShell from the extracted package:

```powershell
.\Install-ContextControl.ps1
```

The installer copies the package to `%LOCALAPPDATA%\Programs\ContextControl`, keeps an existing user settings file during updates, creates a Start Menu shortcut, and launches the app.

Optional switches:

```powershell
.\Install-ContextControl.ps1 -DesktopShortcut
.\Install-ContextControl.ps1 -InstallWebView2Runtime
.\Install-ContextControl.ps1 -NoLaunch
```

`-InstallWebView2Runtime` uses `winget` to install Microsoft Edge WebView2 Runtime if it is not detected. The Local LLM and dependency pages do not need preinstalled LLM runtimes; install them from inside ContextControl.

The setup EXE also supports quiet install testing:

```powershell
.\ContextControl-win-x64-Setup.exe /quiet /installDir=C:\Tools\ContextControl /noLaunch
```
