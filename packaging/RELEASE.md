# ContextControl Release Build

Use this to create the GitHub-ready Windows package:

```powershell
.\packaging\Publish-ContextControlRelease.ps1
```

Local output:

- `.tmp\release\ContextControl-win-x64\`
- `.tmp\release\ContextControl-win-x64.zip`
- `.tmp\release\ContextControl-win-x64.zip.sha256.txt`
- `.tmp\release\ContextControl-win-x64-Setup.exe`
- `.tmp\release\ContextControl-win-x64-Setup.exe.sha256.txt`

The setup EXE is the public installer. It embeds the full staged app folder, opens a Windows installer UI, lets the user choose the install folder, creates Start Menu or desktop shortcuts, registers a per-user Windows uninstall entry, optionally installs Microsoft Edge WebView2 Runtime, keeps existing user settings during updates, writes `install.log`, and launches the app when selected.

The zip is kept as a local portable payload and smoke-test artifact. It includes:

- `ContextControl.Workbench.exe`
- runtime `.dll`, `.deps.json`, `.runtimeconfig.json`, and native build files needed beside the EXE
- `.ccWorkbench.settings.json` seeded from `packaging\release-settings.template.json`
- `Install-ContextControl.ps1`
- `Start-ContextControl.cmd`
- `INSTALL.md`
- `release-manifest.json`

Quiet install smoke test:

```powershell
.\.tmp\release\ContextControl-win-x64-Setup.exe /quiet /installDir="$env:TEMP\ContextControlSetupTest" /noLaunch /noStartMenu
```

Quiet uninstall smoke test:

```powershell
& "$env:TEMP\ContextControlSetupTest\ContextControl.Uninstall.exe" /uninstall /quiet
```

No LLM weights, dependency runtimes, chat history, source files, tests, or build folders are bundled. The app installs dependencies and downloads local LLMs from inside the Dependencies and Local LLM pages.

Current app-side autosetup coverage documented in the README and packaged install guide:

- 17/17 dependency cards expose an installer path
- 262/301 catalog entries use local Ollama model pulls
- 11/301 catalog entries use non-Ollama managed/backend setup
- 28/301 catalog entries are Ollama Cloud entries with no local weight download
- 12/12 image-generation catalog entries have a route

Skillbook is packaged only as draft data right now; the desktop Skillbook feature is not currently usable.

The GitHub Actions workflow `.github/workflows/contextcontrol-release.yml` builds the installer on `workflow_dispatch` and attaches the setup EXE plus checksum to releases when a `v*` tag is pushed. The zip is not required for end-user install.
