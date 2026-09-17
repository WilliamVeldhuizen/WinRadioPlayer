# WinRadioPlayer

Internet radio for Windows (.NET 10 + WinUI 3 / Windows App SDK).

## Download

Windows 10 (version 2004) or later:

- [**WinRadioPlayer-x64.msi**](https://github.com/WilliamVeldhuizen/WinRadioPlayer/releases/latest/download/WinRadioPlayer-x64.msi) for most PCs
- [WinRadioPlayer-arm64.msi](https://github.com/WilliamVeldhuizen/WinRadioPlayer/releases/latest/download/WinRadioPlayer-arm64.msi) for ARM devices (e.g. Snapdragon laptops)

All versions are on the [Releases](https://github.com/WilliamVeldhuizen/WinRadioPlayer/releases) page. The installer is not digitally signed, so Windows SmartScreen may warn you: choose **More info** → **Run anyway**.

## Why

Many stations play an ad as soon as you connect. With a regular radio player that happens every time you switch stations.
This player keeps the streams of your favorites (up to 20) **always open and muted**. When you click a favorite,
its stream is simply unmuted. You are instantly live in the broadcast, without reconnecting and without a pre-roll ad.

It does cost bandwidth: each favorite is a continuous stream of roughly 64 to 320 kbit/s, so 20 favorites usually add up to about 2 to 4 Mbit/s (at most a little over 6).

## Features

- Station list: the newest `stations-yyyy-MM-dd.rsd` from http://rb2rs.freemyip.com/ (~52,000 stations), stored locally for offline use.
- Search by name, genre or country, plus a per-country filter. `qmusic` also finds "Q music".
- When a country is selected, its most popular stations (by [radio-browser.info](https://www.radio-browser.info/) play count) are listed first.
- Add favorites with the star, and reorder them by dragging.
- Each favorite shows the song it is playing right now (or "Advertisement" during an ad break), for stations that send Shoutcast/Icecast titles.
- `Ctrl+Space` to stop or play, `Ctrl+M` to mute or unmute, `Ctrl+F` to search.
- Right-click the taskbar button to switch to a favorite (each shown with its current song) or to mute and unmute, without switching to the window.
- `.pls`, `.m3u` and `.asx` playlists are resolved to the actual stream. HLS (`.m3u8`) is played directly.
- Dropped or stalled streams reconnect automatically.
- A station that is not a favorite plays temporarily and stops when you switch. If you make it a favorite while it plays, the stream stays open.

## Build and run

```powershell
dotnet build
dotnet run --project src/WinRadioPlayer
dotnet test
```

Settings and the station cache are stored in `%LOCALAPPDATA%\WinRadioPlayer`.

## Building the installer

```powershell
.\build-installer.ps1                         # artifacts\installer\WinRadioPlayer-1.0.0-x64.msi
.\build-installer.ps1 -Version 1.1.0 -Arch arm64
```

The MSI (WiX 6) installs the app in `Program Files\WinRadioPlayer` and adds a Start menu shortcut.
.NET and the Windows App SDK are included, so nothing else needs to be installed on the target PC.
An MSI with a higher `-Version` replaces the old installation. Your favorites are kept.
The MSI is not digitally signed, so Windows SmartScreen may ask for confirmation the first time.

To publish a release, push a version tag. The [Release workflow](.github/workflows/release.yml) builds the x64 and ARM64 installers and attaches them to a new GitHub release:

```powershell
git tag v1.1.0
git push origin v1.1.0
```

## Structure

- `src/WinRadioPlayer.Core`: downloading and parsing the station list, search, playlist resolving, the local relay that reads song titles from the streams and settings. No UI, fully tested.
- `src/WinRadioPlayer`: WinUI app. `Playback/RadioEngine` manages the muted streams, `Playback/StationStream` is a single `MediaPlayer` with reconnect logic.
- `tests/WinRadioPlayer.Core.Tests`: xUnit tests.
- `installer`: WiX project for the MSI (not in the solution, build it with `build-installer.ps1`).

## License

[MIT](LICENSE). The station list and popularity data are downloaded at runtime from rb2rs and [radio-browser.info](https://www.radio-browser.info/) and are not part of this repository.
