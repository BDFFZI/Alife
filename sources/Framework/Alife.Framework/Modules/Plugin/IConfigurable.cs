namespace Alife.Framework;

public interface IConfigurable<in T> where T : new()
{
    public void Configure(T configuration);
}
