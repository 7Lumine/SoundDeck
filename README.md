# SoundDeck

SoundDeck is a Windows desktop soundboard and microphone mixer for Discord and other voice chat apps.

It mixes your microphone with MP3 clips and sends the result to a virtual audio device such as VB-CABLE or SteelSeries Sonar.

## What It Does

- Mixes microphone audio and MP3 clips
- Plays multiple clips at the same time
- Supports per-clip play, stop, seek, volume, loop, pin, rename, and hotkey settings
- Supports microphone ducking while clips are playing
- Supports monitor output for checking MP3-only or mixed audio locally
- Saves devices, volume, clips, names, hotkeys, and settings

SoundDeck does not inject audio directly into Discord. It outputs audio to a Windows playback device. For Discord, use a virtual audio cable and select its recording side as the Discord input device.

## Requirements

- Windows 10 or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- A virtual audio device, for example:
  - [VB-CABLE](https://vb-audio.com/Cable/)
  - SteelSeries Sonar
  - VoiceMeeter

The simplest setup is VB-CABLE.

## Download And Run

1. Download `SoundDeck-0.1.1-win-x64.zip` from the latest GitHub Release.
2. Extract the zip file to any folder.
3. Run `SoundDeck.exe`.

Do not run only the `.exe` after moving it somewhere else. SoundDeck currently needs the files next to it, including `SoundDeck.dll`, `SoundDeck.runtimeconfig.json`, and `NAudio.*.dll`.

## VB-CABLE Setup

1. Install VB-CABLE.
2. Start SoundDeck.
3. In SoundDeck, set:
   - `マイク`: your normal microphone
   - `VC出力`: `CABLE Input`
4. In Discord, set:
   - Input device: `CABLE Output`
5. Press `送出開始` in SoundDeck.
6. Add MP3 clips and play them from the soundboard.

The route is:

```text
Microphone + MP3 clips
  -> SoundDeck
  -> CABLE Input
  -> CABLE Output
  -> Discord / OBS / voice chat app
```

## Recommended Discord Settings

If clips sound too quiet, cut off, or distorted in Discord, try:

- Turn off or reduce noise suppression
- Disable automatic input sensitivity and set it manually
- Disable echo cancellation if it affects clip playback
- Disable automatic gain control if the volume pumps up and down

## OBS Test

To test without Discord:

1. Set SoundDeck `VC出力` to `CABLE Input`.
2. In OBS, add an audio input capture source.
3. Select `CABLE Output`.
4. Start SoundDeck pipe and play a clip.

If OBS records clean audio, SoundDeck and VB-CABLE are working and any remaining issue is likely in the voice chat app settings.

## Troubleshooting

### The App Does Not Start

Install the .NET 8 Desktop Runtime:

https://dotnet.microsoft.com/download/dotnet/8.0

If you downloaded a zip, extract it first. Running from inside the zip preview can fail.

### Sound Is Crackling Or Cutting Out

Open `設定` in SoundDeck and try increasing `VC出力` latency:

- Start with `200 ms`
- If it still crackles, try `300 ms`

Also make sure Windows, VB-CABLE, and SoundDeck are using 48 kHz where possible.

### I Cannot Hear The MP3 Locally

Use the `モニター` section:

- `MP3`: hear only MP3 clips
- `Mix`: hear microphone + MP3 mix

Do not set monitor output to the same device as VC output unless you know what you are doing.

### Discord Receives No Audio

Check the device pairing:

- SoundDeck `VC出力`: `CABLE Input`
- Discord input device: `CABLE Output`

Also make sure `送出開始` is active.

## Where Files Are Stored

SoundDeck copies added clips into:

```text
%AppData%\SoundDeck\Clips
```

Settings are stored in:

```text
%AppData%\SoundDeck\settings.json
```

Diagnostic logs are written to:

```text
%AppData%\SoundDeck\sounddeck.log
```

## Build From Source

For development, install the .NET 8 SDK and run:

```powershell
dotnet restore .\SoundDeck.sln
dotnet build .\SoundDeck.sln
dotnet run --project .\src\VoicePipe\SoundDeck.csproj
```

To publish a local build:

```powershell
dotnet publish .\src\VoicePipe\SoundDeck.csproj -c Release -o .\artifacts\SoundDeck-win-x64
```

## Notes

- Ask for permission before playing audio into a shared voice chat.
- SoundDeck currently depends on external virtual audio devices such as VB-CABLE.
- A future version may provide a more integrated output path, but the current 0.1.x line is designed around VB-CABLE/Sonar-style routing.
