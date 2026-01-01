# Win98Get

A Windows GUI for `winget`, with two frontends:

- **Retro**: WinForms app styled to feel like a classic Windows 98 utility
- **Modern**: WinUI 3 app

If you want extremely detailed documentation (including definitions of nearly every term used, even basic ones), see:

- docs/internal-docs.html

## Requirements

- Windows with `winget` installed (Windows Package Manager)
- .NET SDK (the project targets `net10.0-windows`)

Term notes (very explicit):
- “WinForms” (Windows Forms) is a Windows-only UI framework in .NET.
- “winget” is a command-line tool (CLI) that manages packages (apps) on Windows.
- “.NET SDK” is the developer toolchain (compiler + `dotnet` command).
- `net10.0-windows` is the “target framework” (which .NET + OS APIs the app is built for).

## Run

From the repo root:

```powershell
dotnet run --project .\Win98Get
```

To run the Modern app:

```powershell
dotnet run --project .\Win98Get.Modern
```

Term notes:
- `dotnet` is the .NET command-line tool.
- `run` builds (compiles) the project if needed, then launches the app.
- `--project` points to which project folder to run.

## Publish (single EXE, Windows)

Win98Get can be published as a **single self-contained .exe** (no .NET install required on the target machine).

Term notes:
- “Publish” means “produce output you can copy somewhere else to run”.
- “Self-contained” means the published output includes the .NET runtime.
- “Single EXE” (single-file publish) means output is bundled into one executable file.

```powershell
./scripts/publish-win-x64-singlefile.ps1
```

The output will be in:

```text
Win98Get\bin\Release\net10.0-windows\win-x64\publish\Win98Get.exe
```

Notes:
- The published EXE still requires **winget** to be installed on the target machine. If winget is missing, the app shows an error and disables the UI.

The repo also includes `./scripts/publish-and-prune.ps1`, which publishes both apps and writes packaged output under `dist/` (which is intentionally not committed).

## Features

- **Menus**
  - **File**: Refresh, Install all updates (when available), Quit
  - **View**: Output Log (toggle)
  - **Help**: About

- **Installed tab**
  - Lists installed packages (`winget list`) with **Name**, **Id**, **Version**, **Available**, **Source**
  - **Install Update** button prompts first, then runs `winget upgrade --id <id>`
  - **Install All** button prompts first, then runs `winget upgrade --all`
  - **Uninstall** button prompts first, then runs `winget uninstall --id <id>`

- **Search tab**
  - Searches packages (`winget search <query>`) and lists results
  - Press **Enter** in the search box to search
  - Shows a short description when available by calling `winget show --id <id>` for the selected row
  - **Install** button prompts first, then runs `winget install --id <id>`

- **Output log**
  - Hidden by default; toggle it via **View → Output Log**.
  - A persistent textbox shows verbose `winget` output (install/upgrade/uninstall). It is not cleared after commands complete.
  - A **Cancel** button on the top button rows stops the current operation and terminates the underlying `winget` process.

- **Right-click menus**
  - Right-click rows in the grids for actions like Install/Update/Uninstall, Show details, Copy Id/Name, and best-effort Run/Show file location for installed apps.

## Notes

- Winget output is parsed from its table format (your installed `winget` version does not support JSON output for `list/search`).
- Install/upgrade/uninstall are launched as interactive `winget` processes so any installer UI can appear normally.
