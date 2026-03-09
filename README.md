# BaumDash

A personal ultrawide desktop dashboard for Windows. Borderless, dark-themed, always-on-top — built with WinForms and .NET 8.

Designed for a **1920×720 ultrawide monitor**, BaumDash sits at the top or bottom of your screen and gives you a single-glance view of audio, media, calendar, Discord, and system stats — all in one persistent panel.

---

<img width="1919" height="719" alt="image" src="https://github.com/user-attachments/assets/80d1574a-a1d4-40ad-bdc3-250a9d917cbf" />


## Features

| Panel | What it does |
|-------|-------------|
| **Audio Devices** | Switch default output device, control master volume, toggle mic mute |
| **App Volume** | Per-app volume sliders with live session icons (system tray style) |
| **Media** | Album art, track info, prev/play/next — works with anything SMTC-aware (Spotify, browsers, etc.) |
| **Discord** | Live voice channel member list, mic mute toggle, Go Live button |
| **App Shortcuts** | Configurable grid of app launcher tiles |
| **Calendar** | Upcoming events from any iCal/Google Calendar URL |
| **Home Assistant** | Light toggles and live sensor readouts via Nabu Casa cloud |
| **Weather** | Current conditions and temperature via Open-Meteo (no API key needed) |
| **AI Chat** | Quick-access chat against ChatGPT or a local AnythingLLM workspace |
| **PC Stats** | CPU, RAM, GPU, and disk usage at a glance |
| **Clock** | Live time display in the media panel |

---

## Screenshots

> Coming soon

---

## Requirements

- Windows 10 22621+ (Windows 11 recommended)
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- nmap installed for network features (optional)

---

## Installation

Download the latest installer from the [Releases](https://github.com/Bruiserbaum/BaumDash/releases) page and run `BaumDash-Setup-x.x.x.exe`.

The installer places config template files next to the executable. Edit them to enable optional integrations before launching.

---

## Configuration

All config files live next to `BaumDash.exe`. Each integration is opt-in — leave a file blank or with placeholder values to disable that feature.

### Discord (`discord-client-id.txt`)
```
YOUR_CLIENT_ID
```
Create an application at [discord.com/developers](https://discord.com/developers/applications), copy the Client ID, and paste it into this file (one line, no quotes).

### Home Assistant (`ha-config.json`)
```json
{
  "url": "https://your-ha.ui.nabu.casa",
  "token": "YOUR_LONG_LIVED_ACCESS_TOKEN",
  "lights": [
    { "id": "light.living_room", "name": "Room Light" }
  ],
  "sensors": [
    { "id": "sensor.living_room_temperature", "name": "Room Temp" }
  ]
}
```

### Google Calendar (`general-config.json`)
```json
{
  "icalUrls": [
    "https://calendar.google.com/calendar/ical/YOUR_CALENDAR_ID/basic.ics"
  ]
}
```

### Weather (`weather-config.json`)
```json
{
  "lat": 47.6,
  "lon": -122.3,
  "unit": "f"
}
```
Uses [Open-Meteo](https://open-meteo.com/) — no API key required.

### ChatGPT (`chatgpt-config.json`)
```json
{
  "apiKey": "sk-...",
  "model": "gpt-4o"
}
```

### AnythingLLM (`anythingllm-config.json`)
```json
{
  "url": "http://localhost:3001",
  "apiKey": "YOUR_API_KEY",
  "workspace": "your-workspace-slug"
}
```

---

## Building from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (for the installer only).

```bash
# Build
cd WinUIAudioMixer/WinUIAudioMixer
dotnet build

# Run
dotnet run

# Build installer (from WinUIAudioMixer/installer/)
build-installer.bat
```

---

## Architecture

```
WinUIAudioMixer/
├── Controls/           # Owner-drawn WinForms panels (one per dashboard section)
├── Services/           # Audio, Discord, HA, Calendar, Weather, AI, etc.
├── Interop/            # Raw COM interop for Core Audio APIs
├── Models/             # Config record types
├── AppTheme.cs         # Single source of truth for all colours and fonts
└── MainForm.cs         # Root 4-column TableLayoutPanel host
```

The layout is a fixed 4-column `TableLayoutPanel` (300 + 500 + 400 + fill px). Controls are entirely owner-drawn with GDI+ — no external UI libraries.

Audio interfaces run on the WinForms STA thread. All change events are marshalled back to the UI via `SynchronizationContext`. SMTC (media session) is initialised asynchronously on `Form.Load`.

---

## License

Personal use. Not currently open for contributions.
