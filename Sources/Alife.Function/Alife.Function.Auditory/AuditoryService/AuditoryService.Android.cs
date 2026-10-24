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

        recorder ??= new SystemAudioRecorder();
        recorder.WaveformReady -= OnAndroidWaveform;
        recorder.WaveformReady += OnAndroidWaveform;
        await recorder.StartAsync("mic", DestroyCancellationToken);
        IsRunning = true;
        SetListeningState(true);
    }

    public void StopRecording()
    {
        if (IsRunning == false)
            return;

        recorder?.Dispose();
        recorder = null;
        try
        {
            micRecognizer.Flush();
        }
        catch
        {
        }
        IsRunning = false;
        SetListeningState(false);
    }

    private partial bool StartPushToTalkMonitor() => false;

    private partial void StopPushToTalkMonitor()
    {
    }

    SystemAudioRecorder? recorder;

    void OnAndroidWaveform(float[] samples, int count) => micRecognizer.AcceptWaveform(samples, count);
}
#endif
