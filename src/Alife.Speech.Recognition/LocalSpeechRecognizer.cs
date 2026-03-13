using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vosk;
using NAudio.Wave;

namespace Alife.Speech.Recognition;

public class LocalSpeechRecognizer : IDisposable
{
    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private WaveInEvent? _waveIn;
    
    public event EventHandler<(string text, float confidence)>? SpeechRecognized;
    public event EventHandler<string>? SpeechHypothesized;

    public LocalSpeechRecognizer(string modelPath)
    {
        Vosk.Vosk.SetLogLevel(0);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000.0f);
        _recognizer.SetMaxAlternatives(0);
        _recognizer.SetWords(true);
    }

    public void StartListening()
    {
        if (_waveIn != null) return;

        _waveIn = new WaveInEvent();
        _waveIn.WaveFormat = new WaveFormat(16000, 1); // 16kHz Mono
        _waveIn.DataAvailable += (s, e) =>
        {
            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                var resultJson = _recognizer.Result();
                var result = JsonSerializer.Deserialize<VoskResult>(resultJson);
                if (result != null && !string.IsNullOrWhiteSpace(result.text))
                {
                    // Vosk small model doesn't always provide confidence per result in the simple API, 
                    // but it is generally much higher than SAPI.
                    // We simulate high confidence for clear results.
                    SpeechRecognized?.Invoke(this, (result.text, 0.95f));
                }
            }
            else
            {
                var partialJson = _recognizer.PartialResult();
                var partial = JsonSerializer.Deserialize<VoskPartialResult>(partialJson);
                if (partial != null && !string.IsNullOrWhiteSpace(partial.partial))
                {
                    SpeechHypothesized?.Invoke(this, partial.partial);
                }
            }
        };

        _waveIn.StartRecording();
    }

    public void StopListening()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }

    public void Dispose()
    {
        StopListening();
        _recognizer.Dispose();
        _model.Dispose();
        GC.SuppressFinalize(this);
    }

    private class VoskResult
    {
        public string text { get; set; } = "";
    }

    private class VoskPartialResult
    {
        public string partial { get; set; } = "";
    }
}
