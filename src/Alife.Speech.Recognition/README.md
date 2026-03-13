# Alife.Speech.Recognition

High-accuracy local Speech-to-Text (STT) library using the **Vosk** neural engine.

## Features
- **Offline Recognition**: No internet or API keys required.
- **Neural Model**: Uses `vosk-model-small-cn` for accurate Chinese speech recognition.
- **Real-time Feedback**: Provides intermediate (hypothesized) results while the user is still speaking.
- **Audio Capture**: Integrated microphone support via `NAudio`.

## Dependencies
- `Vosk` (NuGet)
- `NAudio.WinMM` (NuGet) - for Windows audio capture
- `Vosk Model`: Requires a model folder (e.g., `model/`) containing the neural network files.

## Simple Usage
```csharp
using Alife.Speech.Recognition;

string modelPath = "path/to/model";
using var recognizer = new LocalSpeechRecognizer(modelPath);

recognizer.SpeechHypothesized += (s, text) => {
    Console.WriteLine("Hearing: " + text);
};

recognizer.SpeechRecognized += (s, e) => {
    Console.WriteLine("Final: " + e.text + " (Confidence: " + e.confidence + ")");
};

recognizer.StartListening();
// ... wait ...
recognizer.StopListening();
```

## Model Note
This library expects a Vosk model folder. You can download models from [alphacephei.com/vosk/models](https://alphacephei.com/vosk/models).
