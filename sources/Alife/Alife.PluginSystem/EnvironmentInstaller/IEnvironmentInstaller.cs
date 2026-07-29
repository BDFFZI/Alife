using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.PluginSystem;

public interface IEnvironmentInstaller
{
    /// <summary>
    /// 安装依赖环境清单
    /// key：包名。可能重复。
    /// value：版本要求.可能冲突。允许如下几种形式：
    ///     >=x.x.x : 最小版本
    ///     &lt;=x.x.x : 最大版本
    ///     ==x.x.x : 精确版本（同时限制上下限）
    ///     空字符串 : 等于 >=0.0.0（无限制）
    /// </summary>
    /// <param name="environment"></param>
    public Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment);
}
