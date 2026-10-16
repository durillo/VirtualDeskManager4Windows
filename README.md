# Virtual Desk Manager

**English** · [Español](README.es.md)

A virtual desktop and window manager for **Windows 11**. It names your desktops and brings them back
if Windows loses them, takes each application to the desktop where you want it, puts windows back
where they were when you reconnect your screens, snaps windows into zones and gives you keyboard
shortcuts for everything, plus a console that drops down from the top of the screen.

It runs as a normal user: **it never asks for administrator rights, installs no drivers and never
gets inside other programs**.

Downloads are under [Releases](../../releases).

> The application itself is available in ten languages, including English and Spanish. Some
> comments in the source code are in Spanish.

---

## Download

| File | What it is |
|---|---|
| `VirtualDeskManager-<version>.msi` | **The installer, recommended.** Installs for your user only, without administrator rights, and updates itself. |
| `VirtualDeskManager-<version>.exe` | Portable version: a single file that runs without installing. It does not update itself. |

Both include .NET: **there is nothing else to install**.

- **Stable releases** have no stage in their name (`1.0.0`). These are the recommended ones.
- **Betas** carry the stage in their name (`1.0.0-beta.4`) and are marked as *Pre-release*. They
  let you try what is new before it becomes stable, and may have bugs.

### Windows warning when you open it

For now the installers **do not carry a certificate Windows recognises**; we are working on it.
Until then you may see:

- **SmartScreen** (“Windows protected your PC”): click **More info** and then **Run anyway**.
- **Smart App Control**, if you have it on, may block it without offering to run it. In that case
  wait for the signed release, or build it yourself (see below).

---

## Requirements

- **Windows 11, 64-bit.** It has not been tested on Windows 10.
- A normal user account. Administrator rights are not needed.

---

## Getting started

1. Install the `.msi`, or open the portable `.exe`.
2. Virtual Desk Manager sits in the **system tray**, next to the clock. Its icon shows the number of
   the desktop you are on.
3. **Click the icon** to open the main window:
   - **My desktops**: one card per desktop with thumbnails of its windows. Click to go there, drag
     a window to move it to another desktop, and use the “…” menu to rename or remove a desktop.
     The “+” card creates a new one.
   - **Automations**: “when *this application* opens, take it to *this desktop*”, maximized,
     always on top or on every desktop.
   - **Arrange windows**: zones to snap windows into on each screen.
   - **Settings**: everything else, with a search box (**Ctrl+F**) and an explanation of every
     option (**F1**).
4. The window in front of you shows **a button of its own in its title bar**, next to minimize:
   always on top, on every desktop, move to another desktop, or “always open this application
   here”.

Closing the window does not close Virtual Desk Manager: it stays in the tray. To quit, use the
tray icon menu.

---

## What it does

- **Named desktops that don't get lost.** If Windows loses your desktops, they are restored with
  their names at startup.
- **Create and remove desktops** from *My desktops* or from the overview of every desktop. Removing
  one never closes a window: its windows move to the neighbouring desktop.
- **Automations** so each application opens where you want it. They are applied once, when the
  window appears.
- **Zones** per screen: drag a window with **Shift** held down to snap it into place, with no gaps.
- **Remembers where every window was** and puts it back when you wake from sleep or plug a screen
  back in.
- **Overview of every desktop** with live thumbnails: switch desktops, move windows by dragging
  them, close them, and create or remove desktops.
- **Drop-down console**, Quake-style, with the shell you choose and a pin to keep it down.
- **Taskbar marks** showing each application's desktop.
- **Configuration backups**: automatic, to OneDrive or Google Drive (as a folder) or to a file.
- **Updates** from within the application, with every release's signature checked before
  installing. It never installs anything without asking, unless you tell it to.
- **10 languages**: Spanish, English, French, Portuguese, Russian, Chinese, Hindi, Bengali, Urdu and
  Arabic.

### Default shortcuts

All of them can be changed in **Settings › Keyboard shortcuts**.

| Shortcut | What it does |
|---|---|
| `Ctrl + Alt + ←` / `→` | Previous / next desktop |
| `Ctrl + Shift + 1…9` | Go straight to that desktop |
| `Ctrl + Alt + Shift + ←` / `→` | Take the active window to the previous / next desktop |
| `Ctrl + Alt + ↑` | Overview of every desktop |
| `Win + Ctrl + ↑` / `↓` | Open / close Windows Task View |
| `Ctrl + Alt + T` | Active window always on top |
| `Ctrl + Alt + P` | Active window on every desktop |
| `Ctrl + Shift + º` | Drop down / roll up the console (the key below Escape on a Spanish keyboard) |
| `Ctrl + Alt + Shift + º` | Pin / unpin the console |

---

## Good to know

- **Shortcuts another program already uses.** A global shortcut wins over the application's own
  (for example, Excel also uses `Ctrl + Shift + number`). If a shortcut shows in red, another
  program has it: change it.
- **Administrator windows.** A window opened “as administrator” cannot be acted on without being
  one, and Virtual Desk Manager never asks to be.
- **Windows updates.** Moving windows between desktops relies on an internal part of Windows. If an
  update changes it, the application keeps working in a basic mode and tells you what is switched
  off.
- **Some applications** (Teams, for example) draw their own border, and a one-pixel line may remain
  between two snapped windows that cannot be removed from outside.

The configuration lives in `%LOCALAPPDATA%\VirtualDeskManager` (`settings.json`, `hotkeys.json`,
`rules.json` and the logs in `logs\`). **Settings › Tools › Log** shows what is happening live, in
case something does not work.

---

## Building

You need the **.NET 10 SDK** on Windows.

```powershell
dotnet build
dotnet test
dotnet run --project src\VirtualDeskManager.App
```

To build the installer and the portable executable, with the runtime included:

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add --global WixToolset.UI.wixext/5.0.2
wix extension add --global WixToolset.Util.wixext/5.0.2

.\installer\build.ps1              # the .msi
.\installer\build.ps1 -Portable    # the .exe
```

The output goes to `dist\`.

### How it is built

Three projects, with a clear rule between them:

- **`VirtualDeskManager.Core`**: the logic, with no Win32 or COM, which is why it has tests
  (`tests/VirtualDeskManager.Core.Tests`).
- **`VirtualDeskManager.Windows`**: all the Windows P/Invoke and COM.
- **`VirtualDeskManager.App`**: the WPF application.

More detail in [docs/arquitectura.md](docs/arquitectura.md) (in Spanish).

---

## License

**[PolyForm Noncommercial 1.0.0](LICENSE.md)**. Free for any **non-commercial** use: individuals,
study, personal projects, charities, educational institutions and public administrations, with the
right to modify and redistribute it.

- **Using it at a company counts as commercial use**, even if nothing is sold or charged for it.
  That needs separate permission from the author.
- It is not free software in the OSI sense (it is *source available*), which is why GitHub shows the
  license as “Other”.

Copyright © 2026 Durillo.
