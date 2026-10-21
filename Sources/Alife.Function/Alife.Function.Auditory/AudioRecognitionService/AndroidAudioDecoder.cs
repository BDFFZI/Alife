#if ANDROID
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using global::Android.Media;

namespace Alife.Function.Auditory;

public static class AudioDecoder
{
    public static float[] DecodeFileTo16kMonoFloat(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("音频文件不存在。", filePath);
        // WAV can contain raw PCM, for which MediaCodec need not provide a decoder.
        using (var file = File.OpenRead(filePath))
        using (var binary = new BinaryReader(file))
        {
            if (file.Length >= 12 && new string(binary.ReadChars(4)) == "RIFF")
            {
                binary.ReadInt32();
                if (new string(binary.ReadChars(4)) == "WAVE") return DecodeWave(binary);
            }
        }
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(filePath);
        MediaFormat? format = null;
        string? mime = null;
        for (int i = 0; i < extractor.TrackCount; i++)
        {
            var candidate = extractor.GetTrackFormat(i);
            string? type = candidate.GetString(MediaFormat.KeyMime);
            if (type?.StartsWith("audio/", StringComparison.Ordinal) == true)
            { format = candidate; mime = type; extractor.SelectTrack(i); break; }
            candidate.Dispose();
        }
        if (format == null || mime == null)
            throw new NotSupportedException("Android 无法解码此音频格式（SILK 需单独解码器）。");
        using (format)
        using (var codec = MediaCodec.CreateDecoderByType(mime)!)
        {
            codec.Configure(format, null, null, MediaCodecConfigFlags.None);
            codec.Start();
            try
            {
                var result = new List<float>();
                using var info = new MediaCodec.BufferInfo();
                int rate = format.GetInteger(MediaFormat.KeySampleRate);
                int channels = format.GetInteger(MediaFormat.KeyChannelCount);
                int encoding = (int)Encoding.Pcm16bit;
                var resampler = new StreamingResampler(rate, channels);
                bool inputDone = false;
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    if (timer.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("音频解码超时。");
                    if (!inputDone)
                    {
                        int index = codec.DequeueInputBuffer(10000);
                        if (index >= 0)
                        {
                            using var input = codec.GetInputBuffer(index)!;
                            input.Clear();
                            int size = extractor.ReadSampleData(input, 0);
                            if (size < 0) { codec.QueueInputBuffer(index, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); inputDone = true; }
                            else { codec.QueueInputBuffer(index, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None); extractor.Advance(); }
                        }
                    }
                    int outputIndex = codec.DequeueOutputBuffer(info, 10000);
                    if (outputIndex == (int)MediaCodecInfoState.OutputFormatChanged)
                    {
                        using var decoded = codec.OutputFormat;
                        rate = decoded.GetInteger(MediaFormat.KeySampleRate);
                        channels = decoded.GetInteger(MediaFormat.KeyChannelCount);
                        encoding = decoded.ContainsKey(MediaFormat.KeyPcmEncoding) ? decoded.GetInteger(MediaFormat.KeyPcmEncoding) : (int)Encoding.Pcm16bit;
                        resampler = new StreamingResampler(rate, channels);
                    }
                    else if (outputIndex >= 0)
                    {
                        try
                        {
                            if (info.Size > 0)
                            {
                                using var output = codec.GetOutputBuffer(outputIndex)!;
                                output.Position(info.Offset); output.Limit(info.Offset + info.Size);
                                byte[] bytes = new byte[info.Size]; output.Get(bytes);
                                Append(bytes, encoding, rate, resampler, result);
                            }
                        }
                        finally { codec.ReleaseOutputBuffer(outputIndex, false); }
                        if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) break;
                    }
                }
                return result.ToArray();
            }
            finally { codec.Stop(); }
        }
    }

    static float[] DecodeWave(BinaryReader reader)
    {
        int rate = 0, channels = 0, bits = 0, tag = 0;
        byte[]? pcm = null;
        var file = reader.BaseStream;
        while (file.Position + 8 <= file.Length)
        {
            string id = new(reader.ReadChars(4));
            uint length = reader.ReadUInt32();
            long next = file.Position + length + (length % 2);
            if (next > file.Length + 1) throw new InvalidDataException("WAV 数据被截断。");
            if (id == "fmt " && length >= 16)
            {
                tag = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (id == "data") pcm = reader.ReadBytes(checked((int)length));
            file.Position = next;
        }
        int encoding = tag == 1 && bits == 16 ? (int)Encoding.Pcm16bit : tag == 3 && bits == 32 ? (int)Encoding.PcmFloat : 0;
        if (encoding == 0 || pcm == null || rate <= 0 || channels <= 0) throw new NotSupportedException("WAV 目前支持 PCM16 或 Float32。");
        var result = new List<float>();
        Append(pcm, encoding, rate, new StreamingResampler(rate, channels), result);
        return result.ToArray();
    }

    static void Append(byte[] bytes, int encoding, int rate, StreamingResampler resampler, List<float> result)
    {
        int width = encoding == (int)Encoding.PcmFloat ? 4 : encoding == (int)Encoding.Pcm16bit ? 2 :
            throw new NotSupportedException($"不支持的 PCM 编码: {encoding}");
        float[] input = new float[bytes.Length / width];
        for (int i = 0; i < input.Length; i++) input[i] = width == 4 ? BitConverter.ToSingle(bytes, i * 4) : BitConverter.ToInt16(bytes, i * 2) / 32768f;
        float[] output = new float[checked((int)Math.Ceiling(input.Length * 16000.0 / rate) + 2)];
        int count = resampler.Process(input, input.Length, output);
        for (int i = 0; i < count; i++) result.Add(output[i]);
    }
}
#endif