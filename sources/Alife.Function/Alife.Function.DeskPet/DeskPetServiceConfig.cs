namespace Alife.Function.DeskPet;

public record DeskPetServiceConfig
{
    public string ModelName { get; set; } = "Mao";
    public int BubbleDurationPerCharMs { get; set; } = 300;
}
