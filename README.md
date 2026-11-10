# Steam Achievement Manager

[![Build](https://github.com/chavdar12/SteamAchievementManager/actions/workflows/build.yml/badge.svg)](https://github.com/chavdar12/SteamAchievementManager/actions/workflows/build.yml)

> **This is a modified fork** of [gibbed/SteamAchievementManager](https://github.com/gibbed/SteamAchievementManager), the original Steam Achievement Manager by Rick (gibbed). It is not the original software. Please report issues with this fork here, not upstream.

Steam Achievement Manager (SAM) is a lightweight, portable application used to manage achievements and statistics in the popular PC gaming platform Steam. This application requires the [Steam client](https://store.steampowered.com/about/), a Steam account and network access. Steam must be running and the user must be logged in.

[Download latest release](https://github.com/chavdar12/SteamAchievementManager/releases/latest).

## Changes in this fork

- A single app: the game picker and the per-game manager are one `SteamAchievementManager.exe` (self-contained, no .NET install needed).
- Targets .NET 10 instead of .NET Framework 4.8.
- The game picker shows games as a list instead of tiles.
- Multiple games can be selected (Ctrl/Shift+click or **Select All**) and opened together: pressing Enter or double-clicking opens one manager window per selected game.
- **Unlock All Achievements** unlocks every achievement in all selected games in one click. Games are processed one at a time in the background; protected achievements are skipped. Can also be run from the command line: `SteamAchievementManager.exe <appid> --unlock-all`.
- Builds and releases are produced by GitHub Actions.

## Building

Requires the .NET 10 SDK:

```
dotnet publish src/SteamAchievementManager -c Release -o publish
```

This produces a single `publish/SteamAchievementManager.exe`.

Project layout (`src/SteamAchievementManager/`):
- `API/`: wrappers around Steam's native `steamclient.dll`.
- `Picker/`: the game list window.
- `Game/`: the per-game achievement and statistics manager.
- `Program.cs`: entry point; with no arguments it opens the picker, with an app ID it opens the manager for that game. It is 32-bit because it loads Steam's 32-bit `steamclient.dll`. Every push to `master` builds it and publishes a GitHub release.

## History

SAM was originally a closed-source application released in 2008 by Rick (gibbed), with its last major release in 2011 and a last update in 2013 (a hotfix). Its author later open-sourced the code so that those interested can do as they like with it. Changes made upstream since the last closed-source release include:
- General code maintenance to bring it into a more modern state.
- Icons have been replaced with ones from the Fugue Icons set.
- Version has been bumped to 7.0.x.x to indicate the open-source release.

## License and attribution

- Original code © Rick (gibbed), released under the [zlib License](LICENSE.txt). This fork keeps that license and its notices unchanged; modifications in this fork are released under the same license.
- Most (if not all) icons are from the [Fugue Icons](https://p.yusukekamiyamane.com/) set by Yusuke Kamiyamane, licensed under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/).
