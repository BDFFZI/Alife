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

    private DateTime _lastPartialTime = DateTime.MinValue;
    private string _lastPartialText = "";
    
    /// <summary>静音自动断句时间（毫秒）。调小可以加快响应，调大可以防止长句断裂。</summary>
    public int SilenceTimeoutMs { get; set; } = 800;

    public void Start()
    {
        if (_waveIn != null) return;

        _waveIn = new WaveInEvent();
        _waveIn.WaveFormat = new WaveFormat(16000, 1);
        _waveIn.DataAvailable += (s, e) =>
        {
            AcceptWaveform(e.Buffer, e.BytesRecorded);
        };

        _lastPartialTime = DateTime.Now;
        _waveIn.StartRecording();
    }

    public void AcceptWaveform(byte[] buffer, int bytesRecorded)
    {
        if (_recognizer.AcceptWaveform(buffer, bytesRecorded))
        {
            FinalizeResult();
        }
        else
        {
            var partialJson = _recognizer.PartialResult();
            var partial = JsonSerializer.Deserialize<VoskPartialResult>(partialJson);
            if (!string.IsNullOrWhiteSpace(partial?.partial))
            {
                if (partial.partial != _lastPartialText)
                {
                    _lastPartialText = partial.partial;
                    _lastPartialTime = DateTime.Now;
                    OnPartial?.Invoke(partial.partial);
                }
                else if ((DateTime.Now - _lastPartialTime).TotalMilliseconds > SilenceTimeoutMs)
                {
                    // 超过静音阈值，强制获取结果
                    FinalizeResult();
                }
            }
        }
    }

    private void FinalizeResult()
    {
        var resultJson = _recognizer.Result();
        var result = JsonSerializer.Deserialize<VoskResult>(resultJson);
        _lastPartialText = "";
        _lastPartialTime = DateTime.Now;

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
                confidence = 1.0f;
            }

            OnRecognized?.Invoke(result.text, confidence);
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
