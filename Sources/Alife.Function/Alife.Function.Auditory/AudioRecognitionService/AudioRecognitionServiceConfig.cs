using System.ComponentModel;

namespace Alife.Function.Auditory;

public class AudioRecognitionServiceConfig
{
    [DisplayName("推送间隔（秒）")]
    [Description("声音监听识别结果合并推送的基础间隔。")]
    public double PushIntervalSeconds { get; set; } = 30;

    [DisplayName("推送间隔随机抖动（秒）")]
    [Description("在基础间隔上随机加减该值，例如 30 秒基础 + 20 秒抖动 = 每次在 10~50 秒间随机触发。")]
    public double PushJitterSeconds { get; set; } = 20;
}
