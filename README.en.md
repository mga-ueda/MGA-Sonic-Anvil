# MGA Sonic Anvil

[日本語](README.md)

Hear, cut, mark, and loop game audio — then hand a Wave-only structure to Wwise.

## What makes this app stand out

**A waveform editor and Wave-only EXPORT into Wwise Interactive Music, in one window.**

Fade, normalize, ripple-delete, copy / paste, markers / regions / sample loop, and a spectrogram — decide by ear and commit. You can prep game one-shots and loops here without bouncing between a DAW and a dedicated editor.

**EXPORT talks to Wwise in Wave-only mode.** Turn on WAAPI above the level meter, pick a destination, and EXPORT writes the source wave into Originals and imports it as a Music Playlist Container (markers / sample loop; no Custom Cues). Play -E previews the loop wrap and writes Play post-exit on EXPORT.

**You can also write MP3.** A valid LAME path in Settings uses your `lame.exe`; empty or invalid uses Windows (default 192 kbps). Right-click a tab to export the current edits as Wave or MP3 (several tabs ask for a folder; dirty tabs stay dirty).

**The UI is Japanese / English.** The Japanese locale keeps the current wording, including English labels as they are. The English locale translates the Japanese. Switch it in Settings (the gear). The default is **Auto** (Japanese if the OS is Japanese, otherwise English).

## Manual & download

- Manual: [Japanese](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.ja.html) · [English](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.en.html) · [Hub](https://mga-ueda.github.io/MGA-Sonic-Anvil/)
- New here? Start with the [quick start](https://mga-ueda.github.io/MGA-Sonic-Anvil/manual.en.html#quickstart)
- In-app: transport **Manual (`?`)** (follows the UI language)
- Builds: [Releases](https://github.com/mga-ueda/MGA-Sonic-Anvil/releases)
- Settings: `%LocalAppData%\MGA\MGA Sonic Anvil\` (`settings.json`; not written next to the exe)
- Launch: a second start brings the existing window forward and opens the files you passed
- On startup the app checks GitHub Releases and tells you if a newer build exists (it does not download updates)
- License: [MIT](LICENSE)

Wwise® / Audiokinetic® are trademarks of their respective owners. This tool is unofficial.

LAME is the name of the LAME project and is licensed under the LGPL. This app does not bundle, modify, link, or redistribute it; it only runs a user-supplied `lame.exe` as a process. Obtaining LAME and complying with its license is the user’s responsibility. This tool is unofficial.
