# ContextControl Release Build

Use this to create the GitHub-ready Windows package:

```powershell
.\packaging\Publish-ContextControlRelease.ps1
```

Output:

- `.tmp\release\ContextControl-win-x64\`
- `.tmp\release\ContextControl-win-x64.zip`
- `.tmp\release\ContextControl-win-x64.zip.sha256.txt`
- `.tmp\release\ContextControl-win-x64-Setup.exe`
- `.tmp\release\ContextControl-win-x64-Setup.exe.sha256.txt`

The zip is a full self-contained Windows x64 app folder and includes:

- `ContextControl.Workbench.exe`
- runtime `.dll`, `.deps.json`, `.runtimeconfig.json`, and native build files needed beside the EXE
- `.ccWorkbench.settings.json` seeded from `packaging\release-settings.template.json`
- `Install-ContextControl.ps1`
- `Start-ContextControl.cmd`
- `INSTALL.md`
- `release-manifest.json`

The setup EXE embeds the same staged app folder and opens a Windows installer UI. It lets the user choose the install folder, create Start Menu or desktop shortcuts, optionally install Microsoft Edge WebView2 Runtime, keeps existing user settings during updates, writes `install.log`, and launches the app when selected.

Quiet install smoke test:

```powershell
.\.tmp\release\ContextControl-win-x64-Setup.exe /quiet /installDir="$env:TEMP\ContextControlSetupTest" /noLaunch /noStartMenu
```

No LLM weights, dependency runtimes, chat history, source files, tests, or build folders are bundled. The app installs dependencies and downloads local LLMs from inside the Dependencies and Local LLM pages.

The GitHub Actions workflow `.github/workflows/contextcontrol-release.yml` builds the same zip and setup EXE on `workflow_dispatch` and attaches both to releases when a `v*` tag is pushed.
