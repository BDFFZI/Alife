// using Alife.Abstractions;
// using Alife.Modules.Context;
// using Alife.OfficialPlugins;
//
// [Plugin("框架-语音可视化", "将语音识别和播报过程实时显示在聊天窗口中。")]
// public class SpeechChatMessage : Plugin
// {
//     readonly ChatWindow chatWindow;
//
//     public SpeechChatMessage(ChatWindow chatWindow, SpeechService speechService)
//     {
//         this.chatWindow = chatWindow;
//         speechService.Speaking += OnSpeaking;
//     }
//     async void OnSpeaking(string arg1, Task arg2)
//     {
//         ChatMessage chatMessage = new() {
//             tool = nameof(SpeechChatMessage),
//             content = arg1,
//             isDefaultHiding = true,
//             isInputting = true,
//         };
//         chatWindow.AddMessage(chatMessage);
//
//         await arg2;
//
//         chatMessage.isInputting = false;
//         chatWindow.UpdateMessage(chatMessage);
//     }
// }
