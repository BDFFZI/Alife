namespace Alife.Function.DeskPet;

public record Live2DDeskPetConfig
{
    public string ModelName { get; set; } = "Mao";

    /// <summary>说话口型开关，默认打开。</summary>
    public bool MouthEnabled { get; set; } = true;

    /// <summary>
    /// 真音量口型开关：打开且装有语音插件时按真实语音包络驱动口型；
    /// 关闭时回落到原版字幕噪声口型。默认打开。
    /// </summary>
    public bool SpeechLipSyncEnabled { get; set; } = true;
}