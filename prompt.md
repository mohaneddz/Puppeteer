You are scaffolding a new desktop application called **Puppeteer**.

Puppeteer is a local developer project hub for Windows. It scans user-selected root folders, detects software projects inside nested folders, organizes them by folder/category/technology, and lets the user open persistent terminals inside each project.

## Reference assets

The repository root contains:

`/design`

This folder contains reference images for:

* Puppeteer logo
* Brand identity
* App icon
* Projects page / main UI

Inspect every image in `/design` before implementing the UI.

Treat those images as the visual source of truth.

Do not redesign the application into a generic Material/Fluent/SaaS dashboard. Match the provided identity closely:

* dark charcoal UI
* warm ivory
* amber/gold accent
* subtle blueprint/grid details
* restrained borders
* compact developer-tool layout
* Puppeteer X/marionette-controller branding
* minimal animation
* no excessive gradients/glassmorphism

---

# Technology

Build Puppeteer using:

* .NET 10
* C#
* WPF
* MVVM
* SQLite
* dependency injection using `Microsoft.Extensions.DependencyInjection`
* configuration using `Microsoft.Extensions.Configuration`
* logging using `Microsoft.Extensions.Logging`

Target Windows.

Structure the solution cleanly so terminal handling can later use Windows ConPTY.

Do NOT use Electron, Tauri, Blazor Hybrid, Avalonia, MAUI, or a webview-based UI.

---

# Solution architecture

Create approximately:

```text
Puppeteer.sln

src/
  Puppeteer.App/
  Puppeteer.Core/
  Puppeteer.Infrastructure/

tests/
  Puppeteer.Core.Tests/

design/
  ...existing assets...
```

Responsibilities:

### Puppeteer.App

WPF UI:

* views
* view models
* controls
* themes
* converters
* navigation
* assets

### Puppeteer.Core

Pure application/domain logic:

* project model
* project detection
* project categorization
* root folders
* sessions
* terminal abstractions
* icon discovery abstractions
* interfaces

Must not depend on WPF.

### Puppeteer.Infrastructure

Implementations for:

* filesystem scanning
* filesystem watchers
* SQLite
* process launching
* shell detection
* icon discovery
* Git metadata
* persistent settings

---

# Core product model

Puppeteer works like this:

```text
Add Root
   ↓
Scan recursively
   ↓
Detect projects
   ↓
Infer categories from folder hierarchy
   ↓
Detect technologies
   ↓
Show project library
   ↓
Open terminal in selected project
   ↓
Keep session alive until explicitly stopped
```

Example folder:

```text
Programming/
  Desktop/
    Personal/
      Yoink/
      Puppeteer/

    Clients/
      ClientProject/

  Mobile/
    Personal/
      PharmaTouch/

  Web/
    Hackathons/
      SomeHackathon/
```

Puppeteer should derive useful hierarchy metadata such as:

```text
Domain: Desktop
Category: Personal
Project: Yoink
```

Do not hardcode those folder names. They are metadata inferred from relative folder hierarchy.

---

# Project detection

Create an extensible detector architecture.

Example:

```csharp
public interface IProjectDetector
{
    Task<ProjectDetectionResult?> DetectAsync(
        string directory,
        CancellationToken cancellationToken = default);
}
```

Implement initial detectors for:

* Tauri
* .NET / WPF
* Flutter
* Next.js
* React/Vite
* Node.js
* Qt
* Kotlin / Android
* Rust
* Python
* Godot

Use signals such as:

```text
src-tauri/tauri.conf.json
*.sln
*.csproj
pubspec.yaml
next.config.*
vite.config.*
package.json
CMakeLists.txt
*.pro
build.gradle
build.gradle.kts
Cargo.toml
pyproject.toml
requirements.txt
project.godot
```

A project may have multiple technologies.

Example:

```text
Yoink
Primary: Tauri

Detected:
Tauri
Rust
React
TypeScript
```

Store a primary framework separately from all detected technologies.

---

# Important scanning behavior

Do NOT recursively inspect heavyweight/generated directories.

Ignore at minimum:

```text
.git
node_modules
.next
dist
build
target
bin
obj
.venv
venv
.idea
.vs
.dart_tool
coverage
```

Initial scan:

```text
root
→ recursive directory traversal
→ identify likely project roots
→ detect technologies
→ build project index
→ save metadata
```

Later architecture should support `FileSystemWatcher` updates without rescanning the whole root.

Do not implement an overcomplicated watcher system yet. Scaffold it cleanly.

---

# Project icons

Default behavior:

Use the icon of the detected primary technology.

Examples:

```text
Tauri → Tauri
.NET → .NET
Flutter → Flutter
Next.js → Next.js
Qt → Qt
Rust → Rust
Python → Python
```

Create an icon provider abstraction.

The project details UI must support:

```text
Change
Find
Reset
```

### Change

Allow manual file selection.

### Find

Search the selected project for likely project icons.

Prefer names:

```text
icon.*
logo.*
app-icon.*
app_icon.*
appicon.*
favicon.*
```

Supported formats should include at minimum:

```text
png
jpg
jpeg
webp
avif
ico
svg
```

Search likely folders first:

```text
/
public/
assets/
src/assets/
app/
src-tauri/icons/
Resources/
Assets/
```

Rank square/near-square images above banners and screenshots.

Show discovered candidates to the user rather than silently picking one.

### Reset

Restore the primary detected framework icon.

Store project icon metadata in Puppeteer's database. Do not modify the project.

---

# Main navigation

Keep the application intentionally simple.

Only expose these top-level destinations:

```text
Projects
Running
Settings
```

Also include an `Add Root` action.

Do NOT create separate top-level pages for:

```text
Dashboard
Git
Commands
Snippets
Environment Manager
SSH Manager
Favorites
Recent
Terminals
Sessions
```

Those either do not belong in v1 or should be contextual.

---

# Projects page

This is the primary screen.

Use `/design` as the visual reference.

Layout:

```text
┌ Sidebar ┐ ┌──────────────── Main ────────────────┐ ┌ Details ┐
│         │ │ Search                              │ │         │
│Projects │ │ Filters                             │ │ Project │
│Running  │ │                                     │ │ details │
│         │ │ Project cards                       │ │         │
│Add Root │ │                                     │ │ actions │
│         │ │                                     │ │         │
│Settings │ │                                     │ │         │
└─────────┘ └─────────────────────────────────────┘ └─────────┘
```

Project cards should show only useful information:

```text
[icon] Puppeteer
       ~/dev/puppeteer

.NET   C#   WPF

>_ Open Terminal
```

Possible small status dot:

* running session
* Git changes

Avoid cramming unnecessary metrics into cards.

---

# Project filtering

Support filters based on metadata such as:

```text
All
Desktop
Mobile
Web
Personal
Client
Hackathon
Running
```

These should be dynamically derived where possible.

Add search.

Search should eventually support metadata combinations such as:

```text
flutter client
desktop personal
running mobile
```

For the scaffold, implement a clean search/filter architecture.

---

# Project details panel

Selecting a project opens the right-side inspector.

Show:

```text
Project icon
Name
Path

Detected stack

[ Open Terminal ]
[ Open Folder ]
[ Open in IDE ]

Git
- branch
- modified file count

Sessions
- active sessions

Project Icon
[ Change ] [ Find ] [ Reset ]
```

Do not create separate pages for this information.

Git integration can initially be lightweight/read-only.

---

# Running page

The Running page should list active terminal/process sessions.

Example:

```text
● Yoink
  pnpm tauri dev
  34m

● PharmaTouch
  flutter run
  12m

● Puppeteer
  dotnet run
  3m
```

Selecting a running session opens its terminal UI.

---

# Terminal architecture

Create abstractions now even if ConPTY is not fully implemented.

Example:

```csharp
public interface ITerminalSession
{
    Guid Id { get; }
    string ProjectPath { get; }
    string Shell { get; }

    Task StartAsync(...);
    Task WriteAsync(string input);
    Task StopAsync();
}
```

And:

```csharp
public interface ITerminalService
{
    Task<ITerminalSession> CreateAsync(...);
    IReadOnlyCollection<ITerminalSession> ActiveSessions { get; }
}
```

The architecture must support later ConPTY integration.

For the initial scaffold, basic process/session functionality may be implemented safely using `Process`, but clearly isolate it behind these interfaces.

A terminal session must use the selected project's path as its working directory.

---

# Terminal UI behavior

Do not permanently occupy half of the Projects page with a terminal.

Normally the UI should remain clean.

When a session is selected/opened:

```text
terminal panel slides/expands from bottom
```

It can then be collapsed while the process remains running.

Allow multiple terminal tabs.

Examples:

```text
dev
build
test
```

---

# Command presets

Project detectors may expose suggested commands.

Examples:

### .NET

```text
dotnet run
dotnet build
dotnet test
```

### Flutter

```text
flutter run
flutter test
flutter pub get
```

### Tauri

```text
pnpm tauri dev
pnpm tauri build
```

### Next.js

Read appropriate scripts from `package.json`.

Never automatically execute detected commands.

They are suggestions only.

---

# Settings

Keep settings minimal.

Sections:

```text
Roots
Terminal
Detection
Appearance
```

### Roots

Add/remove root directories.

### Terminal

Choose default shell:

* PowerShell
* cmd
* Git Bash
* WSL if detected

### Detection

Enable/disable project detectors.

### Appearance

Dark mode is the primary design.

Optional small UI density settings are acceptable.

---

# Persistence

Use SQLite.

Persist at minimum:

```text
RootFolder
Project
DetectedTechnology
ProjectTag
ProjectIcon
TerminalPreset
AppSetting
```

Project records should include:

```text
Id
Name
Path
RootId
PrimaryTechnology
LastOpenedAt
CreatedAt
UpdatedAt
```

Avoid storing information that can cheaply be derived unless useful for caching/indexing.

---

# Design implementation

Inspect `/design` and replicate its visual language.

Create centralized WPF resources:

```text
Themes/
  Colors.xaml
  Typography.xaml
  Controls.xaml
  Cards.xaml
  Buttons.xaml
  ScrollBars.xaml
```

Use reusable controls instead of copying styles everywhere.

Create custom components such as:

```text
ProjectCard
TechnologyBadge
SidebarItem
ProjectInspector
SessionRow
TerminalTab
IconCandidateCard
```

The UI should feel polished immediately even if some deeper functionality is stubbed.

Important:
Do not use default-looking WPF controls.

Restyle:

* buttons
* text boxes
* tabs
* scrollbars
* context menus
* combo boxes
* tooltips

---

# App icon and branding

Use the existing icon/logo assets from `/design`.

Copy the appropriate asset into the application resources and configure it as the WPF app/window icon where possible.

Do not recreate the logo.

---

# Initial sample state

For development/demo purposes, seed mock/sample projects if no root has been configured:

```text
Puppeteer
Yoink
PharmaTouch
InstaDesk
Portfolio
Mobile App
Docs Site
```

Give them different detected stacks so the UI can be evaluated.

Clearly isolate mock data so it disappears once a real root is configured.

---

# Code quality

Requirements:

* nullable reference types enabled
* async APIs for filesystem/database operations where sensible
* cancellation token support
* no giant god classes
* use records/value objects where useful
* dependency injection
* proper interfaces between Core and Infrastructure
* no business logic in code-behind
* minimal code-behind strictly for WPF-specific UI behavior
* MVVM commands
* clean naming
* comments only where they add value

---

# Testing

Add initial unit tests for:

* technology detection
* ignored directory filtering
* hierarchy/category inference
* icon candidate ranking
* project search/filtering

Do not waste time testing trivial property getters.

---

# README

Create a proper README containing:

* Puppeteer description
* architecture
* project structure
* implemented features
* planned ConPTY integration
* how project detection works
* how to build/run
* screenshots section referencing `/design`

---

# Important scope constraint

This is the initial **scaffold**, not an attempt to build an entire IDE.

Prioritize:

1. clean solution architecture
2. working WPF shell
3. polished Projects screen matching `/design`
4. root folder management
5. project scanning/detection
6. SQLite persistence
7. project details
8. icon discovery
9. terminal/session abstractions
10. simple working terminal launching

Do NOT add:

* cloud accounts
* authentication
* subscriptions
* notifications
* collaboration
* AI
* Docker management
* deployment features
* remote SSH tooling
* GitHub integration
* plugin marketplaces

Keep Puppeteer focused:

**Discover projects. Organize them. Open terminals. Hide them. Restore them. Control everything from one place.**

---

Before writing code:

1. Inspect `/design`.
2. Inspect the existing repository, if any.
3. Create the solution architecture.
4. Build the main design system.
5. Implement the Projects screen first.
6. Then implement detection/persistence/services.
7. Make sure the project builds successfully.
8. Fix compiler errors and obvious runtime issues before finishing.

Do not stop at creating empty folders and placeholder classes. Produce a runnable, coherent first scaffold with meaningful functionality.
