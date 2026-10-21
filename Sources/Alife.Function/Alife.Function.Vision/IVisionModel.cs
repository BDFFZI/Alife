using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Vision;

/// <summary>
/// 视觉理解分析器的抽象基类。支持多种具体的视觉模型后端（如本地大模型或在线API）。
/// </summary>
public interface IVisionModel
{
    /// <summary>
    /// 视觉问答：用中文提问，获得中文回答。
    /// </summary>
    Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens, CancellationToken cancellationToken = default);
}
