using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Alife.Framework;
using Alife.Function.MessageFilter;

namespace Alife.Function.Auditory;

[Module("语音识别",
    "为AI增加语音识别能力。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(AuditoryServiceUI))]
public class AuditoryService(
    AIModelUtility.IAuditoryModel auditoryModel,
    Interactor<AuditoryService> interactor,
    MessageFilterService messageFilterService) :
    ChatBehaviour,
    IConfigurable<AuditoryServiceConfig>
{
    public AuditoryServiceConfig Configuration { get; set; } = null!;
    public bool IsRunning { get; private set; }
    public bool IsListening { get; private set; } = true;
    public event Action<bool>? IsListeningChanged;

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
    }
    public void StopRecording()
    {
        graph?.QuantumStarted -= OnQuantumStarted;
        graph?.UnrecoverableErrorOccurred -= OnUnrecoverableErrorOccurred;
        graph?.Stop();

        outputNode?.Dispose();
        outputNode = null;
        inputNode?.Dispose();
        inputNode = null;
        graph?.Dispose();
        graph = null;
        IsRunning = false;
    }

    AudioGraph? graph;
    AudioDeviceInputNode? inputNode;
    AudioFrameOutputNode? outputNode;

    protected override async Task OnStart()
    {
        interactor.ChatTextFilter = text => $"{text}(消息为语音识别结果，错误率较高；建议也用语音功能回复)";

        messageFilterService.RegisterReplyRule(new MessageReplyRule {
            Name = nameof(AuditoryService),
            InputMatching = input => input.Contains(Interactor<AuditoryService>.GetMessageTag()),
            OutputMatching = output => Configuration.OutputTags.Any(tag => output.Contains(tag, StringComparison.OrdinalIgnoreCase)),
            DeviationHandling = () => interactor.Poke($"{nameof(AuditoryService)}消息必须用{string.Join("或", Configuration.OutputTags)}标签回复。如果不想发送消息，也请发送空标签。")
        });

        //开始语言识别
        auditoryModel.Recognized += OnRecognized;
        await StartRecordingAsync();
    }

    protected override Task OnDestroy()
    {
        auditoryModel.Recognized -= OnRecognized;
        StopRecording();
        return Task.CompletedTask;
    }

    unsafe void OnQuantumStarted(AudioGraph sender, object args)
    {
        UpdateListeningState();

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

                if (IsListening) // 按住时发送真实音频
                {
                    fixed (float* dest = samples)
                        Buffer.MemoryCopy(dataInBytes, dest, capacityInBytes, capacityInBytes);
                }

                ThreadPool.QueueUserWorkItem(_ => {
                    auditoryModel.AcceptWaveform(samples);
                });
            }
        }
        finally
        {
            Marshal.Release(ptr);
        }

        void UpdateListeningState()
        {
            string? keyName = Configuration.PushToTalkKey;
            bool newState;
            if (string.IsNullOrEmpty(keyName))
            {
                newState = true;
            }
            else if (Enum.TryParse(keyName, true, out ConsoleKey key))
            {
                newState = (GetAsyncKeyState((int)key) & 0x8000) != 0;

                [DllImport("user32.dll")]
                static extern short GetAsyncKeyState(int vKey);
            }
            else
            {
                newState = true;
            }

            if (newState != IsListening)
            {
                IsListening = newState;
                IsListeningChanged?.Invoke(IsListening);
            }
        }
    }
    void OnUnrecoverableErrorOccurred(AudioGraph audioGraph,
        AudioGraphUnrecoverableErrorOccurredEventArgs audioGraphUnrecoverableErrorOccurredEventArgs)
    {
        StopRecording();
    }
    void OnRecognized(string text)
    {
        interactor.Chat(text);
    }
}