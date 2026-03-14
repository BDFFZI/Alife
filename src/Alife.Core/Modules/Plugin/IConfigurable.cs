public interface IConfigurable<T> where T : new()
{
    public void Configure(T configuration);
}
