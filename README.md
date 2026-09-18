# ZapperRadio

**Instant station switching, no ads when you tune in.**

Internet radio for Windows (.NET 10 + WinUI 3 / Windows App SDK).

## Download

Windows 10 (version 2004) or later:

- [**ZapperRadio-x64.msi**](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-x64.msi) for most PCs
- [ZapperRadio-arm64.msi](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-arm64.msi) for ARM devices (e.g. Snapdragon laptops)

All versions are on the [Releases](https://github.com/WilliamVeldhuizen/ZapperRadio/releases) page. The installer is not digitally signed, so Windows SmartScreen may warn you: choose **More info** → **Run anyway**.

## Why

Many stations play an ad as soon as you connect. With a regular radio player that happens every time you switch stations.
This player keeps the streams of your favorites (up to 20) **always open and muted**. When you click a favorite,
its stream is simply unmuted. You are instantly live in the broadcast, without reconnecting and without a pre-roll ad.

It does cost bandwidth: each favorite is a continuous stream of roughly 64 to 320 kbit/s, so 20 favorites usually add up to about 2 to 4 Mbit/s (at most a little over 6).

## Features

- Station list: the newest `stations-yyyy-MM-dd.rsd` from http://rb2rs.freemyip.com/ (~52,000 stations), stored locally for offline use.
- Search by name, genre or country, plus a per-country filter. `qmusic` also finds "Q music". Typos are forgiven (Levenshtein distance): `radoi 538` finds "Radio 538" and `klasiek` finds "NPO Klassiek". Exact matches are listed first.
- When a country is selected, its most popular stations (by [radio-browser.info](https://www.radio-browser.info/) play count) are listed first.
- Add favorites with the star, and reorder them by dragging.
- Each favorite shows the song it is playing right now (or "Advertisement" in red during an ad break), for stations that send Shoutcast/Icecast titles.
- **Hears music and speech**: the audio of each stream is classified locally with [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1), Google's open-source sound classifier, and shown next to the status ("Live · muted · music", "Now playing · speech"). Songs sound like music almost throughout, while ads, news and presenters mix speech with jingles, so the last half minute is weighed together. It uses the audio that is streamed anyway (no extra bandwidth) and takes about 20 ms of processing per station every 5 seconds. HLS streams are not classified.
- **Zapp on ad breaks / speech**: for listening to music only. When the station you listen to starts an ad break, or a presenter, the news or a talk show is heard, the player zaps to the highest favorite in your list that is playing a song, and zaps back as soon as the station plays a song again. If that favorite starts an ad break or talking too, it zaps on, and it also zaps as soon as a favorite becomes available when none was at the start of the break. Ads are recognized from the ad markers stations send (such as the `adbreak` and `commercial-in` titles of Qmusic and JOE). Stations that do not mark their ads are recognized too: the length of each song is looked up (via the iTunes Search API, in either "Artist - Title" or "Title - Artist" order), and when no new title arrives within 30 seconds after the song should have ended and speech is heard, an ad break is assumed ("Probably an ad break", shown in yellow). If the station keeps playing music, it is just a longer version or a song it sent no title for, and nothing happens; a minute of uninterrupted music also ends an assumed break. Streams that cannot be listened to fall back to the overdue song alone. Favorites that are heard playing music are zapped to even when they send no title (or only a program name), and favorites where someone talks are never zapped to. Picking a station yourself ends the zapping, and if you pick it during its ad break or while someone talks, that break is not zapped away from.
- **Favorite tracks**: hear a song you like? Click the heart next to it at the bottom. The song is saved with the station and date on the **Favorite tracks** tab, next to the **Stations** tab, where you can copy its title or remove it.
- **Compact window**: the button in the title bar shrinks the player to a small window with nothing but your favorites, each with the song it is playing now. Click one to listen. A small indicator per favorite says what it sounds like: a green note while it plays music, a green speech bubble while someone talks, yellow while it is connecting or is probably in an ad break, and red during an ad break or when the stream will not play. The song that is playing has its heart here too, to save it to your favorite tracks. The same button switches back to the full window for searching, adding favorites and the favorite tracks. Both windows keep their own size and position, so switching brings each back the way you left it, and the window you last used is the one you get on the next start.
- **Settings**, behind the gear next to the tabs: the version, which station list is in use and when it was downloaded, with a button to check for a newer one, and a button to clear the cached logos and most-played lists so every logo is looked up again (useful when a station shows the wrong one). The station list itself is kept, since it is the one thing that also works offline.
- `Ctrl+Space` to stop or play, `Ctrl+M` to mute or unmute, `Ctrl+F` to search.
- Right-click the taskbar button to switch to a favorite (each shown with its current song) or to mute and unmute, without switching to the window.
- `.pls`, `.m3u` and `.asx` playlists are resolved to the actual stream. HLS (`.m3u8`) is played directly.
- Dropped or stalled streams reconnect automatically.
- A station that is not a favorite plays temporarily and stops when you switch. If you make it a favorite while it plays, the stream stays open.

## Build and run

```powershell
dotnet build
dotnet run --project src/ZapperRadio
dotnet test
```

Settings and the station cache are stored in `%LOCALAPPDATA%\ZapperRadio`. The app used to be called WinRadioPlayer; an existing `%LOCALAPPDATA%\WinRadioPlayer` folder is moved there on first start.

## Building the installer

```powershell
.\build-installer.ps1                         # artifacts\installer\ZapperRadio-1.0.0-x64.msi
.\build-installer.ps1 -Version 1.1.0 -Arch arm64
```

The MSI (WiX 6) installs the app in `Program Files\ZapperRadio` and adds a Start menu shortcut.
.NET and the Windows App SDK are included, so nothing else needs to be installed on the target PC.
An MSI with a higher `-Version` replaces the old installation. Your favorites are kept.
The MSI is not digitally signed, so Windows SmartScreen may ask for confirmation the first time.

To publish a release, bump `<Version>` in `src/ZapperRadio/ZapperRadio.csproj` and push to `main`. The [Release workflow](.github/workflows/release.yml) runs on every push. When no release exists yet for that version, it builds the x64 and ARM64 installers and attaches them to a new GitHub release tagged `v<version>`.

## Structure

- `src/ZapperRadio.Core`: downloading and parsing the station list, search, playlist resolving, the local relay that reads song titles from the streams, the ad break and music/speech rules, and settings. No UI, fully tested.
- `src/ZapperRadio`: WinUI app. `Playback/RadioEngine` manages the muted streams, `Playback/StationStream` is a single `MediaPlayer` with reconnect logic, `Playback/SoundClassifier` decodes the relayed audio (Media Foundation via NAudio) and runs YAMNet with the ONNX Runtime that comes with the Windows App SDK.
- `tests/ZapperRadio.Core.Tests`: xUnit tests.
- `installer`: WiX project for the MSI (not in the solution, build it with `build-installer.ps1`).

## License

[MIT](LICENSE). The station list and popularity data are downloaded at runtime from rb2rs and [radio-browser.info](https://www.radio-browser.info/) and are not part of this repository. The YAMNet model in `src/ZapperRadio/Assets/Models` is by Google, converted to ONNX by [zeropointnine/yamnet-onnx](https://huggingface.co/zeropointnine/yamnet-onnx), and licensed under the Apache License 2.0.
