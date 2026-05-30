# ContextControl Install Guide

This is a self-contained Windows x64 build. A separate .NET runtime install is not required.

## Recommended Install

Download and run:

```text
ContextControl-win-x64-Setup.exe
```

You do not need a separate zip. The setup EXE contains the full app folder and installs it to the folder you choose.

The setup window lets you:

- choose the install folder
- create Start Menu and desktop shortcuts
- register ContextControl in Windows Installed apps / Apps & features
- optionally install Microsoft Edge WebView2 Runtime for the Browser workspace
- launch ContextControl when setup finishes

Default install folder:

```text
%LOCALAPPDATA%\Programs\ContextControl
```

Run the installed app from the Start Menu shortcut or from:

```text
<install folder>\ContextControl.Workbench.exe
```

Setup writes logs to:

```text
<install folder>\install.log
%LOCALAPPDATA%\ContextControl\install.log
```

## Uninstall

Use one of these:

- Windows Settings -> Installed apps / Apps & features -> ContextControl -> Uninstall
- Start Menu -> ContextControl -> Uninstall ContextControl
- `<install folder>\ContextControl.Uninstall.exe /uninstall`

Quiet uninstall:

```powershell
.\ContextControl.Uninstall.exe /uninstall /quiet
```

Add `/removeUserData` to remove ContextControl logs and user data. The final uninstall log may be recreated under `%LOCALAPPDATA%\ContextControl\uninstall.log`. Local LLM model weights and third-party runtimes such as Ollama are separate programs and are not removed by the ContextControl uninstaller.

If the app fails before its window opens, it writes a crash log to:

```text
<install folder>\ContextControl.Workbench.crash.log
%LOCALAPPDATA%\ContextControl\workbench-crash.log
```

## Quiet Install

For automated testing:

```powershell
.\ContextControl-win-x64-Setup.exe /quiet /installDir=C:\Tools\ContextControl /noLaunch
```

Useful switches:

```text
/installDir=<path>
/desktopShortcut
/noStartMenu
/installWebView2
/noLaunch
```

## Portable Folder

If you already have a full app folder from a local release build, run:

```text
ContextControl.Workbench.exe
```

Keep the files together. The EXE needs the DLLs, runtime files, assets, and `runtimes\` folder beside it.

From a local app folder you can also run:

```powershell
.\Install-ContextControl.ps1
```

Optional script switches:

```powershell
.\Install-ContextControl.ps1 -DesktopShortcut
.\Install-ContextControl.ps1 -InstallWebView2Runtime
.\Install-ContextControl.ps1 -NoLaunch
```

GitHub's automatic source-code zip is not a portable app package. Use it only for building from source.
