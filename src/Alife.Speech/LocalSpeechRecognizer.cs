using System;
using System.Text.Json;
using Vosk;
using NAudio.Wave;

namespace Alife.Speech;

/// <summary>
/// 本地语音识别器（Vosk + NAudio）
/// </summary>
public class LocalSpeechRecognizer : IDisposable
{
    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private WaveInEvent? _waveIn;
    
    /// <summary>识别到完整一句话。参数：(文本, 置信度)</summary>
    public event Action<string, float>? OnRecognized;
    
    /// <summary>识别过程中的中间结果。</summary>
    public event Action<string>? OnPartial;

    public LocalSpeechRecognizer(string modelPath)
    {
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000.0f);
        _recognizer.SetMaxAlternatives(0);
        _recognizer.SetWords(true);
    }

    public void Start()
    {
        if (_waveIn != null) return;

        _waveIn = new WaveInEvent();
        _waveIn.WaveFormat = new WaveFormat(16000, 1);
        _waveIn.DataAvailable += (s, e) =>
        {
            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                var result = JsonSerializer.Deserialize<VoskResult>(_recognizer.Result());
                if (!string.IsNullOrWhiteSpace(result?.text))
                {
                    OnRecognized?.Invoke(result.text, 0.95f);
                }
            }
            else
            {
                var partial = JsonSerializer.Deserialize<VoskPartialResult>(_recognizer.PartialResult());
                if (!string.IsNullOrWhiteSpace(partial?.partial))
                {
                    OnPartial?.Invoke(partial.partial);
                }
            }
        };

        _waveIn.StartRecording();
    }

    public void Stop()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }

    public void Dispose()
    {
        Stop();
        _recognizer.Dispose();
        _model.Dispose();
        GC.SuppressFinalize(this);
    }

    private class VoskResult { public string text { get; set; } = ""; }
    private class VoskPartialResult { public string partial { get; set; } = ""; }
}
