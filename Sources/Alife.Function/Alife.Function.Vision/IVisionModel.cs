using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Vision;

/// <summary>视觉推理服务契约，支持本地模型或云端 API。</summary>
public interface IVisionModel
{
    /// <summary>视觉问答：传入图片路径和问题，返回回答。</summary>
    Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens, CancellationToken cancellationToken = default);
}
