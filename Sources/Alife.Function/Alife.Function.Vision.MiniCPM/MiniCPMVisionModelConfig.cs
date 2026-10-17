namespace Alife.Function.Vision.MiniCPM;

public class MiniCPMVisionModelConfig
{
    /// <summary>
    /// 模型加载精度：int4（NF4量化，省显存，默认）或 bf16（半精度，更快更准）。
    /// </summary>
    public string Precision { get; set; } = "int4";
}
