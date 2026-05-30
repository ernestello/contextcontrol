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

After this version is installed, ContextControl checks GitHub releases on startup when internet is available. Use the **Check updates** button in the header bar to manually check, download, and start a newer setup EXE against the current install folder.

## Local LLM Autosetup

ContextControl does not bundle LLM weights or backend runtimes. Install the app first, then use the **Dependencies** and **Local LLMs** pages.

Current autosetup coverage:

- dependencies: 17/17 app dependency cards have an installer path
- local Ollama model pulls: 262/301 catalog entries
- non-Ollama managed/backend setup: 11/301 catalog entries
- Ollama Cloud entries: 28/301 catalog entries, no local weight download
- image generation: 12/12 image-generation catalog entries have a route

On a raw Windows PC, the Microsoft Store `python.exe` app alias is ignored because it is not a real Python interpreter. Python-backed dependencies such as Diffusers automatically install Python 3.12 through `winget` when no usable Python exists, then create a ContextControl-managed virtual environment.

Still partial/WIP after the first install click: LM Studio server enablement, stable-diffusion.cpp GGUF model file selection through `CC_IMAGE_MODEL_PATH`, bitnet.cpp environment/model setup, RWKV model weight flow, CUDA/WSL/server validation for vLLM/SGLang/TensorRT-LLM/ExLlamaV2, and converted model artifacts for ONNX Runtime GenAI/OpenVINO GenAI.

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
