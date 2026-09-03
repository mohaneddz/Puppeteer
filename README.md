# Puppeteer

Puppeteer is a focused Windows project hub: discover projects beneath chosen roots, organize them by folder hierarchy and technology, open persistent terminals in the correct working directory, hide them, and return later.

![Puppeteer brand identity](design/identity.png)

## What is implemented

- A runnable .NET 10 WPF shell modeled on the supplied charcoal, ivory, and amber references.
- Projects, Running, and Settings navigation with no unrelated dashboard pages.
- Recursive root scanning with generated/heavy directory pruning.
- Extensible detection for Tauri, .NET/WPF, Flutter, Next.js, Vite/React, Node.js, Qt, Android/Kotlin, Rust, Python, and Godot.
- Multiple detected technologies, a separate primary framework, and suggested commands that are never auto-run.
- Dynamic hierarchy filters and multi-term metadata search.
- Project inspector with folder/IDE/terminal actions, lightweight Git status, and change/find/reset icon workflows.
- Ranked icon discovery across common asset folders without modifying project files.
- SQLite persistence for roots, projects, technologies, tags, icons, presets, and app settings.
- Process-backed terminal sessions behind interfaces designed for a future ConPTY implementation.
- Isolated demo projects when no real root exists.

## Architecture

```text
src/
  Puppeteer.App/             WPF views, controls, themes, converters, MVVM
  Puppeteer.Core/            Models, contracts, detection, filtering, hierarchy rules
  Puppeteer.Infrastructure/  Scanning, SQLite, Git, launchers, icons, watchers, processes
tests/
  Puppeteer.Core.Tests/      Behavior-focused unit tests
design/                      Supplied visual source material
```

`Puppeteer.Core` has no WPF dependency. Infrastructure owns OS and storage details. The app composes both through `Microsoft.Extensions.Hosting` and dependency injection. Terminal creation always receives a project path and uses it as the process working directory.

## Detection

The scanner walks each configured root while skipping `.git`, `node_modules`, `.next`, `dist`, `build`, `target`, `bin`, `obj`, virtual environments, IDE state, coverage output, and other generated folders. `SignatureProjectDetector` examines only a candidate directory and returns a primary framework, all technology signals, and safe command suggestions. Adding a specialized detector later only requires another `IProjectDetector` implementation (or a composite registration).

## Build and run

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet restore Puppeteer.sln
dotnet build Puppeteer.sln
dotnet test Puppeteer.sln
dotnet run --project src/Puppeteer.App
```

Runtime data is stored under `%LOCALAPPDATA%\Puppeteer\puppeteer.db`.

## Planned ConPTY work

`ITerminalService` and `ITerminalSession` isolate process lifetime, input, output, and working-directory behavior. The initial implementation uses redirected `Process` streams. A ConPTY-backed session can replace it without changing project discovery, persistence, or view contracts; the next phase should add terminal emulation, resize propagation, richer shell discovery, and session restoration metadata.

## Screenshots and references

The supplied [projects and settings layouts](design/pages.png), [brand identity](design/identity.png), [application icon](design/icon.png), and [controller mark](design/logo.png) are retained in `/design`. The app embeds the supplied icon and mark directly.
