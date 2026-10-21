#if ANDROID
using System;
using System.Threading.Tasks;

namespace Alife.Function.Auditory;

public partial class AuditoryService
{
    public async Task StartRecordingAsync()
    {
        if (IsRunning)
            return;

        if (!string.IsNullOrEmpty(Configuration.PushToTalkKey))
            throw new PlatformNotSupportedException("Android 暂不支持全局键盘按键说话，请清空按键配置。");
        recorder ??= new SystemAudioRecorder();
        recorder.WaveformReady -= OnAndroidWaveform;
        recorder.WaveformReady += OnAndroidWaveform;
        await recorder.StartAsync("mic", DestroyCancellationToken);
        IsRunning = true;
    }

    public void StopRecording()
    {
        recorder?.Dispose();
        recorder = null;
        IsRunning = false;
    }

    SystemAudioRecorder? recorder;

    void OnAndroidWaveform(float[] samples, int count) => auditoryModel.AcceptWaveform(samples, count);
}
#endif
