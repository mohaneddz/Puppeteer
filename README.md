# Puppeteer

Puppeteer is a focused Windows project hub. Point it at the folders where you keep your code, and it discovers every project underneath, groups them by what they are and what they're for, and lets you open persistent terminals in the right working directory without hunting through Explorer.

![Puppeteer brand identity](design/identity.png)

Built with **.NET 10** and **WPF**.

## Features

**Discovery**
- Recursive root scanning that prunes generated/heavy directories (`node_modules`, `bin`, `obj`, `.git`, …).
- Stack detection for Tauri, .NET/WPF, Flutter, Next.js, Vite/React, Node.js, Qt, Android/Kotlin, Rust, Python, and Godot — with a primary framework, the full technology list, and suggested commands.
- Rescan all roots at any time (toolbar button or `Ctrl+R`).

**Organizing**
- Three independent filters: **Type** (Desktop / Website / Mobile / Game), **Technology**, and **Category** (Personal / Client / Hackathon / Course / Work).
- Category is inferred from the folder path, and optionally refined by an LLM (Groq) that reads each project's README — see [AI categorization](#ai-categorization).
- A folder-tree in the sidebar scopes the grid to any subtree.
- Full-text search across name, path and stack, plus sort by name, most-recently-opened, or type.
- Pin the projects you touch most to the top of the grid.

**Working**
- Interactive terminals backed by real processes, opened in the project's directory. Type commands, run detected presets, split panes to see several shells at once, or go fullscreen.
- Stop, restart, clear and copy per session; the terminal panel is resizable and collapsible.
- Open a project in Explorer or your IDE, or copy its path.
- Live Git status: a dot on every card with uncommitted changes, refreshed in the background after each load, and the branch plus changed files in the inspector.

**Living in the tray**
- A notification-area icon that reports how many sessions are live, with Open, New terminal and Quit.
- Minimize and/or close to the tray instead of quitting; a warning before quitting stops running terminals.
- Start with Windows via the per-user Run key — no elevation, revocable from Task Manager's Startup tab — optionally straight to the tray.

**Slice-of-life**
- Grid and list views: cards for browsing, compact rows for scanning a long library.
- Resizable, collapsible sidebar / details panels; the app remembers your window size, panel widths, view, sort and last page.
- Keyboard: `Ctrl+K` search, `Ctrl+N` new terminal, <code>Ctrl+&#96;</code> toggle terminal, `Ctrl+R` rescan, `Esc` clear/close. Shell bindings win while the caret is in a terminal.
- Ranked project-icon discovery across common asset folders, without touching project files.
- Everything (roots, projects, icons, presets, pins, preferences) persists in a local SQLite database.

## Settings

- **Roots** — add and remove the folders Puppeteer scans.
- **Startup & notification area** — launch with Windows, start hidden, minimize to tray, close to tray.
- **Terminal** — default shell, scrollback depth, whether to confirm quitting with sessions running.
- **Appearance** — comfortable or compact card density.
- **Tools** — an Open-in-IDE command (`code`, `rider`, `subl`), or leave it blank for the shell default.
- **AI categorization** — see below.
- **Data** — reset the window layout, or open the folder holding the database.

## AI categorization

Categorizing a project as *personal*, *client*, *hackathon* or *coursework* is done first from the folder path, and — when a key is available — refined by Groq's API using the project's README.

- **Development:** copy `.env.example` to `.env` and set `GROQ_API_KEY`.
- **Anywhere:** paste a key into **Settings → AI categorization**; it overrides the `.env` value and is stored locally.

Without a key, the path-based heuristic is used on its own.

## Running

```bat
run.bat
```

Requires the .NET 10 SDK. The script closes any running instance before rebuilding.

## Project layout

- `src/Puppeteer.Core` — domain models, detection, classification and search (no UI dependencies).
- `src/Puppeteer.Infrastructure` — scanning, SQLite persistence, terminals, Git, and the Groq classifier.
- `src/Puppeteer.App` — the WPF application (MVVM).
- `tests/Puppeteer.Core.Tests` — unit tests for the core logic.
