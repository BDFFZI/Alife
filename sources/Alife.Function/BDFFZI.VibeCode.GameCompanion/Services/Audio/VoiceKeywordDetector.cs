using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.AIModelUtility;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 语音关键词检测器：订阅共享 <see cref="AudioService"/> 的音频数据，
/// 经流式语音识别（IAudioRecognizer）转文字后匹配关键词，供语音采集目标读取。
/// </summary>
public sealed class VoiceKeywordDetector : IDisposable
{
    readonly AudioService audioService;
    readonly IAudioRecognizer? recognizer;
    readonly object gate = new();
    readonly HashSet<string> keywords = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> heardKeywords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>语音识别模型是否可用。不可用时语音采样器不会被创建。</summary>
    public bool IsAvailable => recognizer != null;

    public VoiceKeywordDetector(AudioService audioService, IAudioRecognizerProvider? provider)
    {
        this.audioService = audioService;

        if (provider != null)
        {
            try
            {
                recognizer = provider.CreateAudioRecognizer();
                recognizer.Recognized += OnRecognized;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Companion] 创建语音识别器失败，语音采集将不可用: {ex.Message}");
                recognizer = null;
            }
        }
    }

    /// <summary>
    /// 注册一个待监听的关键词。重复注册幂等；首次注册会订阅音频数据。
    /// </summary>
    public void AddKeyword(string? keyword)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return;
            keywords.Add(keyword.Trim());
        }
        TryStartListening();
    }

    void TryStartListening()
    {
        if (recognizer == null) return;
        audioService.AddListener(OnWaveformReady);
    }

    /// <summary>
    /// 注销一个关键词；注销后无任何关键词时取消订阅音频数据。
    /// </summary>
    public void RemoveKeyword(string? keyword)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return;
            string trimmed = keyword.Trim();
            keywords.Remove(trimmed);
            heardKeywords.Remove(trimmed);
            if (keywords.Count == 0)
                audioService.RemoveListener(OnWaveformReady);
        }
    }

    /// <summary>
    /// 读取并清除指定关键词的命中状态。
    /// </summary>
    /// <returns>"true" 表示自上次读取以来听到了该关键词，否则 "false"。</returns>
    public string Consume(string keyword)
    {
        lock (gate)
        {
            bool heard = !string.IsNullOrEmpty(keyword) && heardKeywords.Remove(keyword);
            return heard ? "true" : "false";
        }
    }

    void OnWaveformReady(float[] samples, int count)
    {
        recognizer?.AcceptWaveform(samples, count);
    }

    void OnRecognized(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (gate)
        {
            if (keywords.Count == 0)
                return;
            foreach (string keyword in keywords)
            {
                if (MatchesKeyword(text, keyword))
                    heardKeywords.Add(keyword);
            }
        }
    }

    /// <summary>
    /// 容错关键词匹配：语音识别结果常含噪声。
    /// 依次尝试：完全包含 → 去噪包含 → 关键词各词项模糊匹配。
    /// </summary>
    static bool MatchesKeyword(string text, string keyword)
    {
        string normalizedText = Normalize(text);
        string normalizedKeyword = Normalize(keyword);
        if (string.IsNullOrEmpty(normalizedKeyword))
            return false;
        if (normalizedText.Contains(normalizedKeyword, StringComparison.Ordinal))
            return true;

        string[] keywordWords = normalizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] textWords = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (keywordWords.Length == 0)
            return false;

        int matched = 0;
        foreach (string kw in keywordWords)
        {
            bool wordHit = textWords.Any(word =>
                word.Contains(kw, StringComparison.Ordinal) ||
                kw.Contains(word, StringComparison.Ordinal) ||
                Levenshtein(word, kw) <= 1 ||
                (kw.Length >= 4 && word.Length >= 4 && (word.StartsWith(kw) || kw.StartsWith(word))));
            if (wordHit)
                matched++;
        }
        return matched >= Math.Max(1, (int)Math.Ceiling(keywordWords.Length * 0.6));
    }

    static string Normalize(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fa5]+", " ").Trim();
    }

    static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        int[] prev = new int[b.Length + 1];
        int[] curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    public void Dispose()
    {
        audioService.RemoveListener(OnWaveformReady);
        if (recognizer != null)
        {
            recognizer.Recognized -= OnRecognized;
            recognizer.Dispose();
        }
    }
}
