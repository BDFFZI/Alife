#if WINDOWS
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace Alife.Function.Auditory;

public partial class AuditoryService
{
    public async Task StartRecordingAsync()
    {
        if (IsRunning)
            return;

        //创建语音专用 AudioGraph（支持回声消除）
        if (graph == null)
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Speech) {
                EncodingProperties = AudioEncodingProperties.CreatePcm(16000, 1, 32)
            };
            settings.EncodingProperties.Subtype = MediaEncodingSubtypes.Float; // 输出 32位 Float，Sherpa 和 Silero 直接可用
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
                throw new Exception($"AudioGraph 创建失败: {result.Status}");
            graph = result.Graph;
        }

        if (outputNode == null)
            outputNode = graph.CreateFrameOutputNode(graph.EncodingProperties);

        //创建语音识别专用输入节点
        if (inputNode == null)
        {
            var inputResult = await graph.CreateDeviceInputNodeAsync(MediaCategory.Speech);
            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
                throw new Exception($"AudioDeviceInputNode 创建失败: {inputResult.Status}");
            inputNode = inputResult.DeviceInputNode;
            inputNode.AddOutgoingConnection(outputNode);
        }

        graph.Start();
        graph.QuantumStarted += OnQuantumStarted;
        graph.UnrecoverableErrorOccurred += OnUnrecoverableErrorOccurred;

        IsRunning = true;
        SetListeningState(true);
    }

    public void StopRecording()
    {
        if (IsRunning == false)
            return;

        graph?.QuantumStarted -= OnQuantumStarted;
        graph?.UnrecoverableErrorOccurred -= OnUnrecoverableErrorOccurred;
        graph?.Stop();

        outputNode?.Dispose();
        outputNode = null;
        inputNode?.Dispose();
        inputNode = null;
        graph?.Dispose();
        graph = null;

        try
        {
            micRecognizer.Flush();
        }
        catch (Exception ex)
        {
            AlifeLog.LogError(ex);
        }

        IsRunning = false;
        SetListeningState(false);
    }

    private partial bool StartPushToTalkMonitor()
    {
        if (string.IsNullOrWhiteSpace(Configuration.PushToTalkKey) ||
            Enum.TryParse(Configuration.PushToTalkKey, true, out ConsoleKey _) == false)
            return false;

        pushToTalkTimer ??= new System.Threading.Timer(_ => _ = UpdatePushToTalkAsync(), null, 0, 50);
        return true;
    }

    private partial void StopPushToTalkMonitor()
    {
        pushToTalkTimer?.Dispose();
        pushToTalkTimer = null;
        if (pushToTalkHeld)
        {
            pushToTalkHeld = false;
            _ = EndTemporaryMicAsync();
        }
    }

    async Task UpdatePushToTalkAsync()
    {
        if (Interlocked.Exchange(ref pushToTalkUpdating, 1) == 1)
            return;

        try
        {
            bool held = false;
            string? keyName = Configuration.PushToTalkKey;
            if (string.IsNullOrWhiteSpace(keyName) == false &&
                Enum.TryParse(keyName, true, out ConsoleKey key))
                held = (GetAsyncKeyState((int)key) & 0x8000) != 0;

            if (held == pushToTalkHeld)
                return;

            pushToTalkHeld = held;
            if (held)
                await BeginTemporaryMicAsync();
            else
                await EndTemporaryMicAsync();
        }
        catch (Exception ex)
        {
            AlifeLog.LogError(ex);
            pushToTalkHeld = false;
            await EndTemporaryMicAsync();
        }
        finally
        {
            Interlocked.Exchange(ref pushToTalkUpdating, 0);
        }
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    System.Threading.Timer? pushToTalkTimer;
    int pushToTalkUpdating;
    volatile bool pushToTalkHeld;

    AudioGraph? graph;
    AudioDeviceInputNode? inputNode;
    AudioFrameOutputNode? outputNode;

    unsafe void OnQuantumStarted(AudioGraph sender, object args)
    {
        if (outputNode == null)
        {
            Console.WriteLine("初始化异常，outputNode为空！");
            return;
        }

        using var frame = outputNode.GetFrame();
        using var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();

        // C#/WinRT 的 IInspectable 在跨越本机 COM 边界时会遇到无法转换回原始接口的 bug
        // 这里通过 CsWinRT 暴露的 NativeObject 获取原生 IUnknown 指针，再通过 QueryInterface 和函数指针调用
        // ReSharper disable once SuspiciousTypeConversion.Global
        IntPtr unk = ((WinRT.IWinRTObject)reference).NativeObject.ThisPtr;
        Guid iid = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"); // IMemoryBufferByteAccess
        if (Marshal.QueryInterface(unk, in iid, out IntPtr ptr) != 0)
        {
            Console.WriteLine("查询音频缓存区COM对象失败！");
            return;
        }

        try
        {
            // vtable 布局: IUnknown 有 3 个方法，GetBuffer 是第 4 个方法 (index 3)
            void** vtable = *(void***)ptr;
            var getBuffer = (delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)vtable[3];

            byte* dataInBytes;
            uint capacityInBytes;
            getBuffer(ptr, &dataInBytes, &capacityInBytes);

            int sampleCount = (int)(capacityInBytes / sizeof(float));
            if (sampleCount > 0)
            {
                float[] samples = new float[sampleCount];
                fixed (float* dest = samples)
                    Buffer.MemoryCopy(dataInBytes, dest, capacityInBytes, capacityInBytes);

                ThreadPool.QueueUserWorkItem(_ => {
                    micRecognizer.AcceptWaveform(samples, sampleCount);
                });
            }
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    void OnUnrecoverableErrorOccurred(AudioGraph audioGraph,
        AudioGraphUnrecoverableErrorOccurredEventArgs audioGraphUnrecoverableErrorOccurredEventArgs)
    {
        StopRecording();
    }
}
#endif
