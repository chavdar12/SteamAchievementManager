# Steam Achievement Manager

[![Build](https://github.com/chavdar12/SteamAchievementManager/actions/workflows/build.yml/badge.svg)](https://github.com/chavdar12/SteamAchievementManager/actions/workflows/build.yml)

> **This is a modified fork** of [gibbed/SteamAchievementManager](https://github.com/gibbed/SteamAchievementManager), the original Steam Achievement Manager by Rick (gibbed). It is not the original software. Please report issues with this fork here, not upstream.

Steam Achievement Manager (SAM) is a lightweight, portable application used to manage achievements and statistics in the popular PC gaming platform Steam. This application requires the [Steam client](https://store.steampowered.com/about/), a Steam account and network access. Steam must be running and the user must be logged in.

[Download latest release](https://github.com/chavdar12/SteamAchievementManager/releases/latest).

## Changes in this fork

- The game picker shows games as a list instead of tiles.
- Multiple games can be selected (Ctrl/Shift+click or **Select All**) and opened together: pressing Enter or double-clicking launches one manager window per selected game.
- Builds and releases are produced by GitHub Actions.

## Building

Requires Visual Studio 2019+ (or MSBuild) with .NET Framework 4.8 targeting:

```
msbuild SAM.sln -restore -p:Configuration=Release -p:Platform=x86
```

Output goes to `upload/`. Pushing a tag like `v7.1.0` builds the project and publishes a GitHub release with the zipped binaries.

## History

SAM was originally a closed-source application released in 2008 by Rick (gibbed), with its last major release in 2011 and a last update in 2013 (a hotfix). Its author later open-sourced the code so that those interested can do as they like with it. Changes made upstream since the last closed-source release include:
- General code maintenance to bring it into a more modern state.
- Icons have been replaced with ones from the Fugue Icons set.
- Version has been bumped to 7.0.x.x to indicate the open-source release.

## License and attribution

- Original code © Rick (gibbed), released under the [zlib License](LICENSE.txt). This fork keeps that license and its notices unchanged; modifications in this fork are released under the same license.
- Most (if not all) icons are from the [Fugue Icons](https://p.yusukekamiyamane.com/) set by Yusuke Kamiyamane, licensed under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/).
