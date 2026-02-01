<p align="center">
  <img width="1000" src="osuautodeafen/Resources/osuautodeafen.png">
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/aerodite/osuautodeafen"></a>
  <img src="https://img.shields.io/github/languages/top/aerodite/osuautodeafen">
  <a href="https://www.codefactor.io/repository/github/aerodite/osuautodeafen">
    <img src="https://www.codefactor.io/repository/github/aerodite/osuautodeafen/badge">
  </a>
  <img src="https://img.shields.io/github/v/release/Aerodite/osuautodeafen">
  <img src="https://img.shields.io/github/downloads/aerodite/osuautodeafen/total">
</p>

##

osuautodeafen is a tool that uses osu! memory data from [Tosu](https://github.com/KotRikD/tosu) to automatically Toggle
Deafen on Discord when certain criteria are reached

# Installation Steps

> [!IMPORTANT]
> Tosu is required to be open while osuautodeafen is running

## Windows

1. Download the [latest release](https://github.com/Aerodite/osuautodeafen/releases/latest) (
   osuautodeafen-win-Portable.zip)
2. Unzip the folder
3. Launch "osuautodeafen.exe" and wait for it to start
4. Set the Deafen Keybind in the app to your Discord "Toggle Deafen" keybind
5. Modify the settings in the app to what you want!

## Linux

1. Download the [latest release](https://github.com/Aerodite/osuautodeafen/releases/latest) (osuautodeafen.AppImage)
3. Launch the AppImage
4. Set the Deafen Keybind in the app to your Discord "Toggle Deafen" keybind*
5. Modify the settings in the app to what you want!

*If you're on Hyprland, you could set the discordClient line in the ~/.config/osuautodeafen/settings.ini to your Discord client and it will instead send the Toggle Deafen keybind straight through the compositor, which should allow the same behavior as Windows or X11.

<details>
  <summary>Linux Info</summary>
Tested on Linux 6.16.4 (CachyOS x86_64, Hyprland) using osu!lazer and osu!stable (through osu-winello) with Tosu from the latest GitHub release running with 'sudo ./tosu'
</details>

# Features

* Works with osu!lazer and osu!stable
* Custom settings for deafening
* Undeafening during Break Periods
* Custom IP and port support for Tosu
* Per-map Settings

> [!NOTE]
> If you want osuautodeafen to work with osu!lazer you must be using v1.0.6 or newer and v4.0.0 of Tosu or newer.

# Dependencies

* [Tosu](https://github.com/KotRikD/tosu)

# Credits

Thank you [Jurme](https://osu.ppy.sh/users/6282195), [InfernoJonk](https://osu.ppy.sh/users/9537557), and [Jarran](https://osu.ppy.sh/users/11417993/osu) for testing (and
discovering) a bunch of edge cases

##

![GitHub Repo stars](https://img.shields.io/github/stars/aerodite/osuautodeafen?style=social)

Please star if this program helped you! <3

