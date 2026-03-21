namespace Alife.Speech
{
    public class LocalSpeechRecognizer : System.IDisposable
    {
        private readonly SherpaOnnx.OfflineRecognizer _recognizer;
        private readonly SherpaOnnx.VoiceActivityDetector _vad;
        private NAudio.Wave.WaveInEvent? _waveIn;
        
        public event System.Action<string, float>? OnRecognized;
        public event System.Action<string>? OnPartial;

        public LocalSpeechRecognizer(string modelPath)
        {
            var config = new SherpaOnnx.OfflineRecognizerConfig();
            config.ModelConfig.SenseVoice.Model = System.IO.Path.Combine(modelPath, "sensevoice-small", "model.int8.onnx");
            config.ModelConfig.Tokens = System.IO.Path.Combine(modelPath, "sensevoice-small", "tokens.txt");
            config.ModelConfig.SenseVoice.Language = "zh"; 
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.ModelConfig.NumThreads = 1;
            config.ModelConfig.Debug = 0;

            _recognizer = new SherpaOnnx.OfflineRecognizer(config);

            var vadConfig = new SherpaOnnx.VadModelConfig();
            vadConfig.SileroVad.Model = System.IO.Path.Combine(modelPath, "silero_vad.onnx");
            vadConfig.SileroVad.Threshold = 0.5f;
            vadConfig.SileroVad.MinSilenceDuration = 0.25f;
            vadConfig.SileroVad.MinSpeechDuration = 0.25f;
            vadConfig.SampleRate = 16000;
            
            _vad = new SherpaOnnx.VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 60);
        }

        public void Start()
        {
            if (_waveIn != null) return;
            _waveIn = new NAudio.Wave.WaveInEvent();
            _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1);
            _waveIn.DataAvailable += (s, e) => AcceptWaveform(e.Buffer, e.BytesRecorded);
            _waveIn.StartRecording();
        }

        public void AcceptWaveform(byte[] buffer, int bytesRecorded)
        {
            var samples = new float[bytesRecorded / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = System.BitConverter.ToInt16(buffer, i * 2) / 32768.0f;

            _vad.AcceptWaveform(samples);
            while (!_vad.IsEmpty())
            {
                var segment = _vad.Front();
                if (segment.Samples != null && segment.Samples.Length > 0) ProcessSegment(segment.Samples);
                _vad.Pop();
            }
        }

        private void ProcessSegment(float[] samples)
        {
            try
            {
                using var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(16000, samples);
                _recognizer.Decode(stream);
                var result = stream.Result;
                string text = CleanResult(result.Text);
                if (!string.IsNullOrWhiteSpace(text)) OnRecognized?.Invoke(text, 1.0f);
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[STT Error] {ex.Message}");
            }
        }

        private string CleanResult(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[.*?\]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<\|.*?\|>", "");
            return text.Trim();
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
            _vad.Dispose();
            System.GC.SuppressFinalize(this);
        }
    }
}
