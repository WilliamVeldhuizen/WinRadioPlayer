# Roadmap

Ideas for what to build next, ranked by payoff per effort. The list is deliberately biased
towards features that only this player can offer, because it keeps every favorite streaming
and already decodes and classifies their audio.

Nothing here is promised or scheduled; it is a working list.

## 1. Live rewind / instant replay (30-60 s ring buffer per favorite)

Every favorite is already decoded for the sound classifier, so keeping the last minute of PCM
per station in a ring buffer costs little extra. It makes it possible to jump back to the start
of the song you zapped into, or to replay what you just missed - across all 20 favorites at once,
which no other radio player can do. Hooks into `IcyProxy`'s audio callback and `StationStream`.
The largest effort on this list and the biggest differentiator.

## 2. Auto-record the current song

`IcyProxy` already knows exactly when a title changes, so track boundaries come for free. A
"record this song" button (and an optional "record everything I heart") writes the segment from
the ring buffer onwards to `%MUSIC%\ZapperRadio\Artist - Title.mp3`, tagged. Together with the
rewind buffer it can record a song that was already halfway through when you noticed it. Intended
for personal use of a broadcast, which the README should say plainly.

## 3. Loudness normalization across stations

The most annoying part of zapping: one station is mastered several dB louder than the next. The
PCM of every stream is decoded anyway, so a running EBU R128-style loudness estimate per station
gives a per-station gain that is applied on unmute, and a manual per-station trim in the settings.
Lives in `StationStream` / `RadioEngine`.

## 4. SMTC, media keys and global hotkeys

There is no `SystemMediaTransportControls` integration yet. Adding it gives the Windows volume
flyout the station logo, artist and title, and makes play/pause and next work from keyboard media
keys and Bluetooth headsets, where "next" maps naturally to zapping to the next favorite. The
existing `Ctrl+Space` and `Ctrl+M` shortcuts only work while the window has focus; registering
them globally (`RegisterHotKey`) makes them work from any app. Low effort, high daily value.

## 5. Tray icon and minimize to tray

The app is built to keep running, yet it only lives in the taskbar. A tray icon showing the
current song in its tooltip, left-click to mute and unmute, right-click for the favorites (the
same content as `TaskbarJumpList` builds) and a "close to tray" option turn it into a background
app instead of a window. Pairs with the media key work above.

## 6. Bandwidth and power guard

The permanent 2 to 6 Mbit/s of background streaming is the one real cost of the design. An eco
mode keeps only the top few favorites open on a metered connection or on battery below a set
percentage, and re-opens the rest on Wi-Fi or AC power. A live "currently using about 3.2 Mbit/s"
readout in the settings makes the cost visible instead of implied. Uses `NetworkInformation` and
the system power status, mostly inside `RadioEngine`.

## 7. Play history - built in 1.10.0

A rolling 12-hour history of everything every favorite played, as a third tab next to Stations and
Favorite tracks, with the title tidied up (`TrackTitle`) and a heart per entry. It is deliberately
kept separate from the rewind and recording features above: `PlayHistory` is a list of titles, not
of audio, so 1 and 2 still have to bring their own buffer.

## 8. Links out to Spotify, YouTube and Apple Music

A favorite track can only be copied as text today. A button per track that opens the song in
Spotify (`spotify:search:` or the web player), YouTube Music or Apple Music closes the loop from
"heard it on the radio" to "saved in my playlist". The iTunes Search API is already called in
`TrackDurations`, so the exact track is often known. Very low effort.

## 9. Smarter zap rules

The zapper is the identity of the app, so give it knobs:

- Per favorite: never zap to this one, or only zap to these. A news station should not be a music fallback.
- A disliked songs list, so it zaps away from a title that was thumbed down.
- Skip the news at the top of the hour, which is predictable and time based.
- After a break ends: return to the station you came from, or stay where you landed.

All of this belongs in `AdBreakZapper` and `AppSettings`, the UI-free and fully tested core, so it
is cheap to build and cheap to test.

## 10. Import and export of favorites, and favorite sets

A shareable JSON (or `.m3u`) of the favorites makes it possible to move machines, keep a backup or
publish a preset. Alongside it, named favorite sets (Work, Weekend, Dance) keep the cap of 20 open
streams while removing the ceiling as a practical limit: only the active set streams.

## Suggested order

Start with 4 and 5: about a day each, and they change how the app feels every day. Then 1, because
it is the feature that cannot be copied without also keeping every stream open.
