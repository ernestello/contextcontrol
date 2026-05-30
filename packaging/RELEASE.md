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

The zip is self-contained for Windows x64 and includes:

- `ContextControl.Workbench.exe`
- `.ccWorkbench.settings.json` seeded from `packaging\release-settings.template.json`
- `Install-ContextControl.ps1`
- `Start-ContextControl.cmd`
- `INSTALL.md`
- `release-manifest.json`

The setup EXE contains the same staged files and runs `Install-ContextControl.ps1` after extraction. It installs to `%LOCALAPPDATA%\Programs\ContextControl`, creates a Start Menu shortcut, keeps existing user settings during updates, and launches the app.

No LLM weights, dependency runtimes, chat history, source files, tests, or build folders are bundled. The app installs dependencies and downloads local LLMs from inside the Dependencies and Local LLM pages.

The GitHub Actions workflow `.github/workflows/contextcontrol-release.yml` builds the same zip and setup EXE on `workflow_dispatch` and attaches both to releases when a `v*` tag is pushed.
