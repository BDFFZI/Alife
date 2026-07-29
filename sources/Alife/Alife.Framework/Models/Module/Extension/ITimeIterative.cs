namespace Alife.Framework;

public interface ITimeIterative
{
    public void OnUpdate(ref float time);
    public float DeltaTime => 1;
}
