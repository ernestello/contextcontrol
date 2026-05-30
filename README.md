# ContextControl

ContextControl is a native desktop workbench for keeping local code context, local models, and patch workflows under the user's control.

The current release focuses on a Windows x64 desktop app that can be opened like a normal EXE, then used to install local LLM dependencies and download model weights on demand. The older PowerShell CLI pipeline is still included as the core deterministic context engine.

## Install On Windows

Latest release:

https://github.com/VulkanVX/contextcontrol/releases/tag/v0.3.0

For a fresh Windows PC, download only the installer:

```text
ContextControl-win-x64-Setup.exe
```

You do not need to download a separate app zip. The setup EXE is a single-file installer that already contains the full ContextControl app folder. It asks for the install location, shortcut options, optional WebView2 Runtime install, and whether to launch when setup finishes.

Default install folder:

```text
%LOCALAPPDATA%\Programs\ContextControl
```

After install, run ContextControl from the Start Menu shortcut or from:

```text
<install folder>\ContextControl.Workbench.exe
```

The app is self-contained, so a separate .NET runtime install is not required.

GitHub's automatic **Source code** downloads are source snapshots, not runnable app packages. Use them only if you want to build from source.

## What Is Bundled

Bundled inside the installer:

- ContextControl Workbench native desktop app
- PowerShell ContextControl CLI scripts
- ContextControl `lib/` modules and default `skillbook/` file
- Release appearance defaults
- Full Windows app folder with runtime files beside the EXE
- Setup UI with install folder picker, shortcut options, logs, and quiet install mode

Not bundled:

- LLM model weights
- Ollama models
- Python packages for model backends
- GPU drivers, CUDA toolkits, or vendor runtimes
- Chat history, project exports, patch files, or local runtime state

Those are created or downloaded only after the user chooses them in the app.

If the app fails before the main window opens, it writes a crash log beside the installed EXE and to `%LOCALAPPDATA%\ContextControl\workbench-crash.log`.

## Current Status

Stable enough to test:

- Windows x64 installer
- Project file tree and project scanner views
- Local LLM catalog
- Dependency detection and one-click installers where safe
- Ollama model pull/remove workflow
- Basic local chat through supported chat-ready models
- Theme and appearance settings

Work in progress:

- Context Control prompting flow in the desktop app
- Skillbook behavior, defaults, and long-term format
- Image generation routes
- Non-Windows packaged releases
- Some advanced GPU/server model backends

The CLI scripts remain the conservative path for the original DIR/CC/GO patch pipeline while the desktop prompting flow matures.

## Local LLM And Dependency Install

The app separates dependencies from model weights.

Dependencies are runtimes and libraries such as Ollama, llama.cpp, Python packages, or backend servers. Models are the actual weights, usually much larger. ContextControl does not download large model weights during app install.

One-click dependency install currently covers these categories when the platform has a safe path:

| Category | Examples |
|---|---|
| Package manager apps | Ollama, LM Studio |
| Managed Python environments | Diffusers, Transformers, MLX LM, MLC LLM, vLLM, SGLang, OpenVINO GenAI, ONNX Runtime GenAI, TensorRT-LLM, ExLlamaV2 / TabbyAPI |
| Native portable downloads | llama.cpp server, KoboldCpp, stable-diffusion.cpp, RWKV Runner |
| Source archive setup | bitnet.cpp source checkout |

Some entries are still manual or partially manual because they depend on drivers, CUDA, platform-specific builds, external accounts, or model licenses. The UI should show those as manual instead of pretending a one-click install is safe.

## Main Workbench Areas

- **Local LLMs**: browse models, see fit notes, pull Ollama models, and route chat/image tasks.
- **Dependencies**: detect installed backends and install managed dependencies.
- **Project Files**: open a project folder and inspect source structure.
- **Project Graph**: visualize project structure and export graph views.
- **Scanner**: summarize project stack, languages, manifests, and important files.
- **Conversation**: use local model chat with ContextControl context where supported.
- **Browser**: embedded WebView2 surface on Windows.

## Context Control Prompting Flow

The original ContextControl pipeline is deterministic:

1. `ccDir.ps1` exports a filtered project tree.
2. `cc.ps1` exports selected files or functions.
3. `ccReplace.ps1` applies explicit `CC-REPLACE` patch blocks.

The desktop app is being built around the same idea, but the prompting flow is still WIP. Treat generated request lines, prompt capsules, and Skillbook-assisted instructions as active development areas.

## Skillbook

`skillbook/` contains draft local-model instruction material. It is useful for experimentation, but it is not a stable public skill format yet. Names, defaults, and activation behavior may change.

## Build From Source

Requirements:

- Windows for the release installer EXE
- .NET 9 SDK
- PowerShell

Run the tests:

```powershell
dotnet run --project .\ide\ContextControl.Workbench.Tests\ContextControl.Workbench.Tests.csproj --configuration Release
```

Run the app from source:

```powershell
dotnet run --project .\ide\ContextControl.Workbench\ContextControl.Workbench.csproj
```

Build release artifacts:

```powershell
.\packaging\Publish-ContextControlRelease.ps1
```

Output goes to:

```text
.tmp\release\
```

The GitHub Actions workflow in `.github/workflows/contextcontrol-release.yml` publishes the Windows installer when a `v*` tag is pushed. The release script also creates a local app-folder zip as the installer payload and for developer smoke testing, but end users only need the setup EXE.

## Repository Layout

```text
.github/workflows/                 Release workflow
ide/ContextControl.Workbench/       Avalonia desktop app
ide/ContextControl.Workbench.Tests/ Focused smoke tests
lib/                                Shared PowerShell pipeline modules
packaging/                          Release and installer scripts
skillbook/                          Draft local-model instruction material
cc*.ps1, cc*.cmd                    CLI entry points
```

Ignored local runtime files include `.tmp/`, `.ccReplace.versions/`, `.ccWorkbench.*`, generated exports, patch files, build output, and user settings.

## Privacy Model

ContextControl is local-first. The app scans projects and runs local child processes on the user's machine. Model weights and dependency runtimes are installed only after the user chooses them. Local LLM backends are separate programs, so review each backend before installing it.
