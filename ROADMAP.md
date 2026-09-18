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

## 4. Lock screen, media keys and global hotkeys - built in 1.11.0

`Shell/SystemMediaControls` owns one `SystemMediaTransportControls` for the app and feeds it from what
the window shows: the station, the song and the station logo in the volume flyout and on the lock
screen, with play, pause, next and previous, which is also what the media keys of a keyboard or a
headset press. Next and previous walk the favorites (`Core/Playback/FavoriteRing`), so the media keys
zap. The per-player overlay stays off in `StationStream`, because twenty players would each claim the
card. A WinUI 3 desktop app has no view to ask, so the controls are obtained for the window handle
through `ISystemMediaTransportControlsInterop`; .NET does not marshal an IInspectable interface, so
its one method is called through the vtable.

`Shell/GlobalHotkeys` claims `Ctrl+Alt+P`, `Ctrl+Alt+M`, `Ctrl+Alt+Right` and `Ctrl+Alt+Left` with
`RegisterHotKey` and watches for WM_HOTKEY by chaining the window procedure. They are on `Ctrl+Alt`
rather than on the `Ctrl+Space` and `Ctrl+M` of the window, because claiming those system wide would
take them away from every other app. A combination another app already holds is named in the settings
instead of failing, and the whole set can be switched off there.

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

## 11. More languages

Every string in the app is English today, written out where it is used. The station list is worldwide
and most of its listeners are not, so the player should speak the language Windows is set to, starting
with the ones the favorites are in: Dutch, German, French and Spanish.

The work is mostly mechanical: an `x:Uid` on each XAML element and a `Resources.resw` per language,
a `ResourceLoader` for the strings that are built in code (`StatusTexts`, `SongTexts`,
`JumpListCommand.Title`, the error messages and the settings texts), and the installer texts on top
of that. The dates and times are pinned to `en-US` in `FavoriteTrack`, `PlayedTrack` and
`MainViewModel`, which should follow the chosen language instead.

Two things are not mechanical. `MainViewModel.AllCountries` is the text "All countries" *and* the
value the country box is compared against to mean "no filter", so as soon as it is translated the
comparisons stop matching for anyone who switches language; it needs a sentinel of its own, separate
from what is shown. And the country and genre names come from the station list in English, so either
they stay English while the rest of the window is translated, or a mapping per language is kept for
the few dozen countries that matter. Neither is hard, but both decide how finished the result feels.

## 12. Start the song clock when the song starts, not when its title arrives

The unmarked ad break detection of item 4 in the README times a song from the moment its title comes
in: `StationStream.WatchSongEndAsync` takes `DateTime.UtcNow` there and sets `_songEndTimer` to
`title arrival + length + SongOverrun`, 30 seconds. That assumes the title and the song start together,
and plenty of stations do not work that way. Their playout system announces the next item while the
current one is still fading, or over the jingle in between, so the title runs 10 to 20 seconds ahead
of the audio.

The clock then starts too early and the 30 seconds of slack quietly shrink to 10. The song is marked
overdue while it is still playing or has only just ended, and the first presenter or station ident
after it is enough for `UnmarkedAdBreak` to call a break that is not one: a yellow "Probably an ad
break", and with zapping on, a zap away from a station that was about to play the next song.

The audio already says when the song really starts, so anchor the clock to that instead: hold the
timer while the stream sounds like speech and start it at the first window of music. The one thing to
get right is that `SetMetadata` calls `_sound.Clear()` on every new title and `SoundHistory.Current`
needs three windows (about 15 seconds) before it says anything, so the decision cannot be read at the
instant the title arrives - it has to come from the windows that follow it, in `AddSound`. A cap on
the wait keeps a quiet or instrumental intro, which classifies as neither, from holding the timer
forever, and streams that are not classified at all (HLS, which skips the relay) keep timing from the
title as they do now.

Worth doing before the slack is tuned: with the clock anchored to the song, `SongOverrun` means what
it says again, and can probably come down, which makes the real breaks show up sooner too.

## Suggested order

With 4 built, 5 is next: it pairs with the media keys and is about a day. Then 12, which is an
afternoon and makes the zapper, the thing the app is named after, wrong less often. Then 1, because
it is the feature that cannot be copied without also keeping every stream open.
