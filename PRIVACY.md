# Virtual Desk Manager privacy policy

**English** · [Español](PRIVACIDAD.md)

*Last updated: 16 November 2026.*

## In short

**Virtual Desk Manager does not collect, send or share any of your data.** It has no user accounts,
no telemetry, no usage statistics, no advertising and no automatic error reports. Everything it
keeps stays on your computer.

## What it keeps on your computer

To work, the application keeps some data **only on your computer**, in the
`%LOCALAPPDATA%\VirtualDeskManager` folder. None of it is sent anywhere:

- **Your configuration**: settings, keyboard shortcuts and automations (`settings.json`,
  `hotkeys.json`, `rules.json`).
- **Where each window was**, to put it back when your screens change: the program name, the kind of
  window, its title and its position.
- **A diagnostic log** (`logs\`), to find out why something is not working. It may include program
  names and window titles. The last 7 days are kept, and it can be turned off in
  *Settings › Tools*.
- **Configuration backups**. If you choose to keep them in OneDrive or Google Drive, Virtual Desk
  Manager only places the file in the folder that program syncs; it does not connect to those
  services. From then on, that file is handled by the service you chose, under its own policy.
- If you turn on **Start with Windows**, an entry in your user's Windows registry.

## Internet connections

The only connection it makes is to **check for and download updates** from `durillo.es/updates`:

- The request carries no identifier and no data about you or your computer: it only asks for the
  files of the latest version.
- As with any request to a website, the server sees the IP address you connect from, and the hosting
  provider may keep it for a while in its technical logs. It is not used for anything else.
- You can turn off automatic checks in *Settings › General › Updates*.

If you download the application from GitHub, that download is covered by GitHub's privacy policy.

## How to delete your data

Uninstall Virtual Desk Manager from *Windows Settings › Apps* (or delete the portable `.exe`) and
delete the `%LOCALAPPDATA%\VirtualDeskManager` folder. If you made backups in OneDrive or Google
Drive, delete them there too.

## Changes to this policy

If the application changes in a way that affects your privacy, this page will be updated and the
date above will change.

## Contact

For any question, open an issue at
[github.com/durillo/VirtualDeskManager4Windows](https://github.com/durillo/VirtualDeskManager4Windows/issues).
