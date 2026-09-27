namespace Alife.Function.DeskPet;

public record Live2DDeskPetConfig
{
    public string ModelName { get; set; } = "Mao";

    /// <summary>说话口型开关，默认打开。</summary>
    public bool MouthEnabled { get; set; } = true;
}