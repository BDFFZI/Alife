namespace Alife.Framework;

public interface IConfigurable
{
    public object Configuration { get; set; }
}

/// <summary>
/// 实现后，系统将自动为实现类接入配置系统。当对象被构造以及配置变动时，会自动填充配置属性，并实现UI联动。在Awake阶段就可以安全读取配置数据了。
/// </summary>
public interface IConfigurable<T> : IConfigurable where T : class, new()
{
    public new T Configuration { get; set; }

    object IConfigurable.Configuration { get => Configuration; set => Configuration = (T)value; }
}
