using System;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.MessageFilter;

namespace Alife.Function.Auditory;

[Module("听觉能力",
    "为AI增加实时语音识别能力（监听麦克风并实时转文字）。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(AuditoryServiceUI))]
public partial class AuditoryService(
    Interactor<AuditoryService> interactor,
    MessageFilterService messageFilterService,
    AIModelUtility.IAuditoryModel auditoryModel) :
    ChatBehaviour,
    IConfigurable<AuditoryServiceConfig>
{
    public AuditoryServiceConfig Configuration { get; set; } = null!;
    public bool IsRunning { get; private set; }
    public bool IsListening { get; private set; } = true;
    public event Action<bool>? IsListeningChanged;

    protected override async Task OnStart()
    {
        //注册回复规则
        if (Configuration.ReplyRestrictedWords.Count != 0)
        {
            messageFilterService.AddMessageReplyRule(new MessageReplyRule {
                Name = nameof(AuditoryService),
                InputMatching = input => input.Contains(Interactor<AuditoryService>.GetMessageTag()),
                OutputMatching = output => Configuration.ReplyRestrictedWords.Any(tag => output.Contains(tag, StringComparison.OrdinalIgnoreCase)),
                CorrectionMessage = () =>
                    $"{nameof(AuditoryService)}消息必须用{string.Join("或", Configuration.ReplyRestrictedWords)}标签回复。如果不想发送消息，也请发送空标签。"
            }, DestroyCancellationToken);
        }

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

    void OnRecognized(string text)
    {
        interactor.Chat(text + "(消息为语音识别结果，错误率较高；建议也用语音功能回复)");
    }
}
