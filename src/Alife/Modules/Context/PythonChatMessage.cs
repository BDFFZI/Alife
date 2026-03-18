// using Alife.Abstractions;
// using Alife.Modules.Context;
// using Alife.OfficialPlugins;
//
// [Plugin("框架-Python可视化", "将 Python 的运行过程实时显示在聊天窗口中。")]
// public class PythonChatMessage : Plugin
// {
//     readonly ChatWindow chatWindow;
//     ChatMessage lastChatMessage = null!;
//
//     public PythonChatMessage(ChatWindow chatWindow, PythonService pythonService)
//     {
//         this.chatWindow = chatWindow;
//         pythonService.PrePythonRun += OnPrePythonRun;
//         pythonService.PostPythonRun += OnPostPythonRun;
//     }
//     void OnPrePythonRun(string obj)
//     {
//         lastChatMessage = new ChatMessage() {
//             tool = nameof(PythonService),
//             content = obj,
//             isInputting = true,
//             isDefaultHiding = true,
//         };
//         chatWindow.AddMessage(lastChatMessage);
//     }
//     void OnPostPythonRun(string obj)
//     {
//         lastChatMessage.content += "\n=====================\n" + obj;
//         chatWindow.UpdateMessage(lastChatMessage);
//     }
// }
