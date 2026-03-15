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
        // Vosk.Vosk.SetLogLevel(0);
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
            AcceptWaveform(e.Buffer, e.BytesRecorded);
        };

        _waveIn.StartRecording();
    }

    public void AcceptWaveform(byte[] buffer, int bytesRecorded)
    {
        if (_recognizer.AcceptWaveform(buffer, bytesRecorded))
        {
            var result = JsonSerializer.Deserialize<VoskResult>(_recognizer.Result());
            if (!string.IsNullOrWhiteSpace(result?.text))
            {
                float confidence = 0.0f;
                if (result.result != null && result.result.Count > 0)
                {
                    float sum = 0;
                    foreach (var word in result.result)
                    {
                        sum += word.conf;
                    }
                    confidence = sum / result.result.Count;
                }
                else
                {
                    confidence = 1.0f; // Default if no word info (some models/configs)
                }

                OnRecognized?.Invoke(result.text, confidence);
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

    private class VoskResult 
    { 
        public string text { get; set; } = ""; 
        public List<VoskWord>? result { get; set; }
    }

    private class VoskWord
    {
        public string word { get; set; } = "";
        public float conf { get; set; }
    }

    private class VoskPartialResult { public string partial { get; set; } = ""; }
}
