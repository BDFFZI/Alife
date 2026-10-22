using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.AIModelUtility;

/// <summary>视觉理解分析器的抽象接口，支持本地模型或在线 API。</summary>
public interface IVisionModel
{
    /// <summary>视觉问答：传入图片路径和问题，返回回答。</summary>
    Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens, CancellationToken cancellationToken = default);
}
