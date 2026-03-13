# Alife

**Alife** is an experimental project focused on creating an interactive "Artificial Life" companion. It combines a flexible XML-based response interpreter with natural speech synthesis and high-accuracy offline speech recognition.

## Project Structure

The solution consists of several modular components:

### Core Components
- **[Alife.Interpreter](src/Alife.Interpreter)**: A streaming XML interpreter that allows granular control over AI responses using custom tag handlers.
- **[Alife.Speech.Recognition](src/Alife.Speech.Recognition)**: High-accuracy local STT using the **Vosk** engine and neural models.
- **[Alife.Speech.Synthesis](src/Alife.Speech.Synthesis)**: Natural-sounding local TTS leveraging **Edge-TTS** and NAudio.

### Tests & Samples
- **[Alife.Interpreter.Test](src/Alife.Interpreter.Test)**: Examples of XML-driven logic and streaming processing.
- **[Alife.Speech.Test](src/Alife.Speech.Test)**: A complete demonstration of a voice-interactive loop with interruption support.

## Getting Started

### Prerequisites

1. **.NET 9 SDK**: Ensure you have the latest .NET environment.
2. **Python 3**: Required for speech synthesis.
   - Install dependencies: `pip install edge-tts`
3. **Vosk Model**: The `Alife.Speech.Recognition` library requires a Vosk model folder (included in the source for Chinese speech).

### Building the Project

Open the solution in Visual Studio 2022 / VS Code or use the CLI:

```powershell
dotnet build Alife.slnx
```

### Running the Voice Demo

```powershell
cd src/Alife.Speech.Test
dotnet run
```

## Features
- **Key-free & Offline**: Optimized for local execution without expensive API subscriptions.
- **User Interruption**: Intelligent speech logic that allows users to talk over the AI.
- **Micro-Animations Friendly**: Designed for "desktop pet" scenarios where low-latency feedback is critical.

## License
MIT
