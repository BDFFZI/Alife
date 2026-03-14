namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Microsoft.SemanticKernel;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
public class SpeechService : IPlugin
{
    readonly LocalSpeechSynthesizer speechSynthesizer;
    LocalSpeechRecognizer? recognizer;
    ChatBot chatBot = null!;
    bool isSpeaking;
    ChatWindow chatWindow;
    readonly string modelPath;

    public SpeechService(InterpreterService interpreterService, ChatWindow chatWindow)
    {
        interpreterService.RegisterHandler(this);
        string assemblyDir = Path.GetDirectoryName(typeof(SpeechService).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        modelPath = Path.Combine(assemblyDir, "model");
        speechSynthesizer = new LocalSpeechSynthesizer();
        this.chatWindow = chatWindow;
    }

    public Task StartAsync(Kernel kernel, ChatBot chatBot)
    {
        this.chatBot = chatBot;
        return Task.CompletedTask;
    }

    [XmlHandler("speak", "让AI说出指定的内容给用户听。")]
    public async Task Speak(string content)
    {
        ChatMessage chatMessage = new ChatMessage() {
            author = "speak",
            content = content,
            isInputting = true
        };
        chatWindow.AddMessage(chatMessage);

        isSpeaking = true;
        try
        {
            await speechSynthesizer.SpeakAsync(content);
        }
        finally
        {
            isSpeaking = false;

            chatMessage.isInputting = false;
            chatWindow.UpdateMessage(chatMessage);
        }
    }
}
