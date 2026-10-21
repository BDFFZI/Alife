#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Speech;

public partial class SpeechService
{
    // Android 直接使用进程内 MediaPlayer，无需客户端注入。
    partial void InitializeAudioPlayer() => audioPlayer = new AndroidSpeechAudioPlayer();

    private partial float[] CreateEnvelope(string filePath) => [];

    private partial Task PlayBufferedAudioAsync(string filePath, CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif
