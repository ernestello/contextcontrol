# CC-DESC: Future Context Control IDE phased implementation plan.

# Context Control IDE — Phase List

This document defines the phased path from the current script-driven Context Control workflow toward a future local IDE/workbench.

The core rule is: do not turn Context Control into a generic VS Code clone.

The future IDE should be change-centered:

- task first
- context second
- patch third
- validation fourth
- file tree last

The main UI object is a Change Capsule: a local, inspectable unit of software evolution containing the goal, evidence, selected files/functions, patch plan, validation results, and history.

---

## Phase 0 — Native desktop shell scaffold

Goal:

Create the first visible native artifact for the future IDE without touching backend behavior.

Deliverables:

- `contextcontrol/ide/PHASELIST.md`
- `contextcontrol/ide/ContextControl.Workbench/`

Rules:

- No backend changes.
- No build changes.
- No script behavior changes.
- No browser, WebView, Electron, Tauri, or HTML UI layer.

Purpose:

Give the project a concrete native target UI so future implementation phases do not drift into a generic editor, terminal wrapper, chat interface, or web page.

---

## Phase 1 — Local workbench shell

Goal:

Create a real app shell around the current Context Control workflow.

Chosen implementation:

- Avalonia desktop app on .NET.
- No HTML shell and no WebView runtime.
- Existing PowerShell scripts stay the backend contract.

Deliverables:

- app entry point
- native desktop window
- project selector placeholder
- project hierarchy sidebar
- file history sidebar
- empty main work area
- mocked project data until the PowerShell bridge is wired

Rules:

- Actions may still be mocked.
- No destructive filesystem operations.
- No patch execution from UI yet.
- Prefer virtualized/native controls over custom rendering unless profiling proves otherwise.

---

## Phase 2 — Change Capsule data model

Goal:

Represent one engineering task as structured local state.

A Change Capsule should contain:

- id
- title
- status
- goal
- evidence
- selected files
- selected symbols
- prompt/context notes
- patch plan
- patch blocks
- build result
- run result
- validation result
- risks
- unknowns
- history

Suggested storage:

```text
contextcontrol/state/capsules/<capsule-id>.json
```

Rules:

- Human-readable.
- Deterministic.
- Easy to diff in Git.
- No hidden cloud dependency.

---

## Phase 3 — Wire existing CC actions

Goal:

Connect the UI to the current Context Control script workflow.

Primary actions:

- DIR
- CONTEXT
- ASK
- PATCH
- BUILD
- RUN
- VERIFY
- COMMIT

Expected behavior:

DIR:

Generate project/file map.

CONTEXT:

Export selected files/functions.

ASK:

Prepare prompt package for the model.

PATCH:

Apply CC-REPLACE blocks.

BUILD:

Run configured build command.

RUN:

Launch configured executable.

VERIFY:

Run configured validation command or parse diagnostics.

COMMIT:

Run Git add/commit with generated or user-provided message.

Rules:

- Existing script behavior should remain valid.
- UI is a layer over the deterministic backend.
- The terminal is available, but not the main UX.

---

## Phase 4 — Inspector and Context Lens

Goal:

Replace the dumb file tree with a relevance-focused inspector.

Inspector sections:

- Files
- Symbols
- Builds
- Risks
- Unknowns
- History
- Metrics
- Patch impact

The inspector should answer:

- Why is this file relevant?
- Which function is being changed?
- What depends on it?
- What is still unknown?
- What validation is missing?

---

## Phase 5 — Patch timeline

Goal:

Make every patch visible as a reversible engineering event.

Each patch record should show:

- patch number
- intent
- files touched
- mode used
- build result
- run result
- validation result
- risk level
- rollback status
- commit status

Purpose:

AI output should never be treated as magic text. It should become an auditable local transformation.

---

## Phase 6 — AI prompt/export workflow

Goal:

Generate compact prompt packages from the selected Change Capsule.

The generated prompt should include:

- task goal
- current evidence
- selected files/functions
- relevant diagnostics
- previous patch result
- exact instruction style
- requested output format

Rules:

- Avoid broad repo dumps.
- Avoid repeated tool/context bloat.
- Prefer small deterministic exports.

---

## Phase 7 — Real editor integration

Goal:

Support code inspection without turning the UI into tab chaos.

Preferred model:

- function fragments
- file excerpts
- diff cards
- call-chain cards
- dependency cards

Avoid:

- opening huge files by default
- forcing the user into a permanent file tree
- hiding the task goal behind editor tabs

---

## Phase 8 — Metrics and validation layer

Goal:

Attach real build/runtime/performance signals to the Change Capsule.

Examples:

- build passed/failed
- compiler error summaries
- runtime diagnostics
- frame timing
- paint brush visual latency
- CPU/GPU bottleneck snapshots
- regression notes

For VulkanVX-style workflows, this is critical.

The IDE should show:

```text id="f0trny"
Before:
visual_complete_ms: 480 ms
GPU frame: 28 ms

After:
visual_complete_ms: 12 ms
GPU frame: 5 ms
```

---

## Phase 9 — Native polish

Goal:

Make the tool pleasant enough to use daily.

Features:

- keyboard shortcuts
- command palette
- collapsible logs
- saved layouts
- light/dark themes
- readable typography
- local settings
- project profiles

The design target is a calm engineering cockpit, not a noisy AI dashboard.

---

## Phase 10 — Public packaging

Goal:

Prepare Context Control IDE for external users.

Deliverables:

- README screenshots
- architecture explanation
- install instructions
- quickstart
- license
- example project
- demo capsules
- release build

Core public message:

Context Control is a deterministic source-context and patch pipeline for working with large codebases through any chat assistant.

The assistant never touches your disk directly.

You export context locally, the assistant writes a mechanical patch recipe, and Context Control applies it locally on your machine.

---

# Design principles

## 1. Change first

The central object is the task/change, not the file.

## 2. Local first

No cloud lock-in. No hidden remote execution.

## 3. Deterministic first

The model suggests. Context Control applies mechanically.

## 4. Context small by default

The UI should push the user toward minimal relevant context.

## 5. Evidence visible

Diagnostics, build errors, and reasoning notes should stay attached to the task.

## 6. Patch auditable

Every change should have a visible intent, result, and rollback path.

## 7. Terminal available, not dominant

The terminal is the backend machine room, not the main user experience.

---

# First implementation target

The first real target is not a complete IDE.

The first target is:

```text id="j0fz3b"
A local native project workbench shell
```

It should visually prove:

- Header project open/create controls
- Open-project switcher
- Project metadata
- Project hierarchy sidebar
- File history sidebar
- Empty main work area

This is implemented in:

```text id="qvqhmz"
contextcontrol/ide/ContextControl.Workbench/
```
