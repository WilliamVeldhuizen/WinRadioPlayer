# ZapperRadio logo concepts

> **Chosen:** `bolt-wave.svg` in its Z-Bolt variant (`bolt-wave/c-z-bolt.svg`).
> It is the app icon, built out in [`app-icon/`](app-icon/).

Twenty candidate marks, one 64x64 SVG each. They all share one small system so they
can be compared fairly:

- Ink `#131A26` for the mark, one accent, zap amber `#F2A93B`, on paper `#F4F2EC`.
- A 64x64 grid with roughly 4px of padding, 3-5px strokes with round caps, so the
  shapes survive at 16px.
- Every mark works as a solid silhouette: swap the ink for white on a dark tile.

| File | Idea |
| --- | --- |
| `bolt-wave.svg` | Broadcast arcs opening around a bolt |
| `zap-dial.svg` | Tuning dial with the bolt as its needle |
| `signal-stack.svg` | Favourites list, one of them unmuted |
| `antenna-z.svg` | A mast throwing a Z instead of a wave |
| `lightning-z.svg` | The letter Z with a lightning diagonal |
| `tuner-bar.svg` | Frequency scale with the marker on your station |
| `zap-button.svg` | Play button with a charge arc |
| `wave-skip.svg` | One waveform hopping to the next |
| `ad-mute.svg` | The talk bubble, struck through |
| `station-ring.svg` | A ring with a gap, the new station in the opening |
| `pulse-row.svg` | A row of open streams, the loud one in the middle |
| `zap-pin.svg` | A location pin for stations, charged |
| `tower.svg` | The classic transmitter |
| `tuning-knob.svg` | A hardware knob with a pointer |
| `zapp-tile.svg` | A finished app tile: bolt between two muted waves |
| `bolt-note.svg` | A quaver whose flag is a bolt |
| `quick-swap.svg` | Two arrows trading places around a live stream |
| `headphone-bolt.svg` | Listening, with the charge between the cups |
| `spectrum.svg` | Level meter, the loudest bar is the one you picked |
| `orbit.svg` | Stations circling one live centre |

## Turning a pick into the app icon

`src/ZapperRadio/Assets/ZapperRadio.ico` needs 16, 24, 32, 48, 64, 128 and 256px
frames. `zapp-tile.svg` is already tile-shaped; any other mark wants a background
tile behind it first (ink tile, amber mark, or the reverse). Render the SVG to PNG
at each size and pack them, for example with ImageMagick:

```powershell
magick logo.svg -background none -define icon:auto-resize=256,128,64,48,32,24,16 ZapperRadio.ico
```
