# Alife.Speech.Synthesis

Natural-sounding local Text-to-Speech (TTS) library using **Edge-TTS** (via Python) and **NAudio**.

## Features
- **Premium Voices**: Uses Microsoft Edge's neural voices (e.g., `zh-CN-XiaoyiNeural`).
- **Lively Personality**: Configured for high-quality, expressive speech output.
- **Cancellation Support**: Ability to stop playback immediately (useful for user interruptions).

## Dependencies
- **Python 3**: Must be installed and in the PATH.
- **edge-tts**: Python package. Install via:
  ```bash
  pip install edge-tts
  ```
- `NAudio.Wave` (NuGet): For MP3 playback.

## Simple Usage
```csharp
using Alife.Speech.Synthesis;

using var synthesizer = new LocalSpeechSynthesizer();

// Simple speech
await synthesizer.SpeakAsync("你好，我是你的桌面小宠物！");

// Stop speech (e.g., when the user interrupts)
synthesizer.Stop();
```

## Configuration
The voice is currently hardcoded to `zh-CN-XiaoyiNeural` in `LocalSpeechSynthesizer.cs` for a lively/youthful feel.
