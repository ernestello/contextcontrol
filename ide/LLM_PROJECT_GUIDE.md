# ContextControl Workbench LLM Project Guide

Use this file as the first stop when asking an LLM to modify the desktop IDE. It names the main source areas, the usual edit targets, and the boundaries that should stay stable.

## Source Map

| Area | Main files | Purpose |
|---|---|---|
| App startup | `App.axaml`, `App.axaml.cs`, `Program.cs` | Avalonia app bootstrapping and desktop lifetime. |
| Main shell | `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `Views/MainWindowParts/**` | Top-level workbench layout and view-specific event bridges. |
| Settings shell | `Views/ThemeSettingsWindow.axaml`, `Views/ThemeSettingsWindowParts/**`, `Views/Settings/**` | Appearance, local LLM, and project rule settings UI. |
| Custom controls | `Controls/**` | Render-heavy controls for code, project tree, graph, chat transcript, model catalog, and dependency lists. |
| Workbench view model | `ViewModels/Workbench/**` | App-level state, project tabs, workspace mode, project tree actions, graph state, settings state. |
| Context Control view model | `ViewModels/ContextControl/**` | Prompt workflow, DIR/CC/GO orchestration, local LLM chat, attachments, terminal output, progress. |
| Project data | `Services/Projects/**`, `Services/ProjectStackScanning/**` | Project loading, file rules, stack detection, scanner output, tree rows. |
| Context pipeline services | `Services/ContextControl/**` | Context request resolution, prompt/capsule building, patch process integration, skillbook. |
| Local LLM services | `Services/LocalLlm/**`, `ViewModels/LocalLlm/**` | Model catalog, Ollama integration, dependency detection/install progress, chat/image protocol. |
| Browser services | `Controls/Browser/**`, `Services/Browser/**`, `ViewModels/Browser/**` | WebView2 host, browser tabs, external browser routing. |
| Styling | `Styles/WorkbenchDesign.axaml`, `Styles/WorkbenchDesign/**` | Theme resources and control styling split by feature area. |
| Tests | `ContextControl.Workbench.Tests/Program.cs` | Focused smoke checks for resolver behavior, markdown parsing, and model catalog state. |

## Edit Rules

- Keep UI layout in XAML/user controls and behavior in the matching `.axaml.cs` bridge or view model partial.
- Prefer extending the existing partial class files instead of creating another large monolithic file.
- Do not put long-running work on the UI thread. Project scans, PowerShell calls, model refreshes, and network/process checks belong in services.
- Treat `lib/**` PowerShell scripts as the CLI/core pipeline. The desktop app orchestrates them; it should not silently fork their behavior.
- Treat `.tmp/`, `bin/`, `obj/`, `.ccReplace.versions/`, `.ccWorkbench.browser-data/`, chat exports, code exports, and `patch.txt` as runtime output.
- Use [UI_ELEMENT_NAMES.md](UI_ELEMENT_NAMES.md) for exact user-facing element names in prompts.

## Fast Prompt Examples

- "Change the prompt window input behavior in `ContextPromptBar`, especially `ContextPromptTextBox`."
- "Modify the project file tree rendering in `ProjectTreeRenderControl`, not the graph view."
- "Add a project rules setting in `FileRulesSettingsPage` and persist it through `WorkbenchViewModel.ProjectRules`."
- "Adjust Local LLM catalog card layout in `LocalLlmCatalogRenderControl`, leaving dependency cards alone."
