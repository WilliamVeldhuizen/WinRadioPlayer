# WinRadioPlayer

Internetradio voor Windows (.NET 10 + WinUI 3 / Windows App SDK).

## Waarom

Veel zenders laten reclame horen zodra je verbinding maakt. Bij gewone radiospelers gebeurt dat bij elke wissel van zender.
Deze speler houdt de streams van je favorieten (maximaal 20) **altijd open en gedempt**. Als je een favoriet aanklikt,
wordt die stream alleen maar hoorbaar gemaakt. Je zit dan meteen live in het programma, zonder nieuwe verbinding en zonder reclame vooraf.

Kost wel bandbreedte: elke favoriet is een doorlopende stream van ongeveer 64 tot 320 kbit/s, dus 20 favorieten zijn samen meestal zo’n 2 à 4 Mbit/s (hooguit ruim 6).

## Functies

- Zenderlijst: de nieuwste `stations-yyyy-MM-dd.rsd` van http://rb2rs.freemyip.com/ (~52.000 zenders), lokaal bewaard voor offline gebruik.
- Zoeken op naam, genre of land, plus een filter per land. `qmusic` vindt ook "Q music".
- Favorieten toevoegen met de ster, volgorde wijzigen door te slepen, `Ctrl+1` … `Ctrl+0` om te wisselen naar favoriet 1 t/m 10.
- `Ctrl+Spatie` om te stoppen of af te spelen, `Ctrl+F` om te zoeken.
- `.pls`, `.m3u` en `.asx` playlists worden omgezet naar de echte stream. HLS (`.m3u8`) wordt direct afgespeeld.
- Verbroken of vastgelopen streams maken automatisch opnieuw verbinding.
- Een zender die geen favoriet is, speelt tijdelijk en stopt als je wisselt. Maak je hem favoriet terwijl hij speelt, dan blijft de stream open.

## Bouwen en starten

```powershell
dotnet build
dotnet run --project src/WinRadioPlayer
dotnet test
```

Instellingen en de zendercache staan in `%LOCALAPPDATA%\WinRadioPlayer`.

## Installer

```powershell
.\build-installer.ps1                         # artifacts\installer\WinRadioPlayer-1.0.0-x64.msi
.\build-installer.ps1 -Version 1.1.0 -Arch arm64
```

De MSI (WiX 6) installeert de app in `Program Files\WinRadioPlayer` en maakt een snelkoppeling in het startmenu.
.NET en de Windows App SDK zitten erin, dus op de doel-pc hoeft niets anders geïnstalleerd te zijn.
Een MSI met een hoger `-Version` vervangt de oude installatie. Je favorieten blijven daarbij behouden.
De MSI is niet digitaal ondertekend, dus Windows SmartScreen kan bij de eerste keer om bevestiging vragen.

## Structuur

- `src/WinRadioPlayer.Core`: ophalen en parsen van de zenderlijst, zoeken, playlists omzetten en instellingen. Geen UI, volledig getest.
- `src/WinRadioPlayer`: WinUI-app. `Playback/RadioEngine` beheert de gedempte streams, `Playback/StationStream` is één `MediaPlayer` met herverbindlogica.
- `tests/WinRadioPlayer.Core.Tests`: xUnit-tests.
- `installer`: WiX-project voor de MSI (niet in de solution, bouwen via `build-installer.ps1`).
