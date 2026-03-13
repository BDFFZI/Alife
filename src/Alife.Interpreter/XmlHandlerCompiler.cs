using System.Reflection;

namespace Alife.Interpreter;

/// <summary>
/// 函数编译器：通过反射扫描 [TagHandler] 方法，构建标签名→调用器的映射表。
/// 编译完成后编译器本身即可丢弃，只需保留其产出的 CompiledHandlerTable。
/// </summary>
public class XmlHandlerCompiler
{
    enum ParameterRole { Context, Content, Attribute }

    sealed record ParameterMapping(
        ParameterRole Role, string? AttributeName, Type ParameterType,
        bool HasDefaultValue, object? DefaultValue);

    readonly List<(object? Target, MethodInfo Method, string TagName)> registrations = new();

    public XmlHandlerCompiler Register(object handlerInstance)
    {
        Type type = handlerInstance.GetType();
        foreach (MethodInfo method in type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            XmlHandlerAttribute? attr = method.GetCustomAttribute<XmlHandlerAttribute>();
            if (attr != null)
            {
                registrations.Add((method.IsStatic ? null : handlerInstance, method, attr.TagName));
            }
        }
        return this;
    }

    public XmlHandlerCompiler Register<T>() where T : class
    {
        foreach (MethodInfo method in typeof(T).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            XmlHandlerAttribute? attr = method.GetCustomAttribute<XmlHandlerAttribute>();
            if (attr != null)
            {
                registrations.Add((null, method, attr.TagName));
            }
        }
        return this;
    }

    public XmlHandlerTable Compile()
    {
        Dictionary<string, CompiledTagInvoker> handlers = new();
        foreach ((object? target, MethodInfo method, string tagName) in registrations)
        {
            ParameterMapping[] mappings = BuildParameterMappings(method);
            object? t = target; MethodInfo m = method;
            
            CompiledTagInvoker invoker = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
            {
                object?[] args = ResolveArguments(mappings, ctx, content, attrs);
                
                object? result = m.Invoke(t, args);
                
                // 回填 ref string (因为 Invoke 会更新 args 数组中的对象)
                for (int i = 0; i < mappings.Length; i++)
                {
                    if (mappings[i].Role == ParameterRole.Content && args[i] is string newContent)
                    {
                        content = newContent;
                        break;
                    }
                }

                if (result is Task task)
                {
                    return task;
                }
                return Task.CompletedTask;
            };

            if (handlers.TryGetValue(tagName, out CompiledTagInvoker? existing))
            {
                CompiledTagInvoker prev = existing;
                handlers[tagName] = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
                {
                    Task t1 = prev(ctx, ref content, attrs);
                    Task t2 = invoker(ctx, ref content, attrs);
                    return Task.WhenAll(t1, t2);
                };
                continue;
            }
            
            handlers[tagName] = invoker;
        }
        return new XmlHandlerTable(handlers);
    }

    // ═══════════════════════════════════════
    //  参数映射
    // ═══════════════════════════════════════

    static ParameterMapping[] BuildParameterMappings(MethodInfo method)
    {
        ParameterInfo[] ps = method.GetParameters();
        ParameterMapping?[] map = new ParameterMapping?[ps.Length];
        bool contentFound = false;

        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType == typeof(XmlTagContext))
            {
                map[i] = new(ParameterRole.Context, null, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                continue;
            }
            
            if (ps[i].GetCustomAttribute<XmlTagContentAttribute>() != null)
            {
                if (ps[i].ParameterType != typeof(string) && ps[i].ParameterType != typeof(string).MakeByRefType())
                {
                    throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name}: [XmlTagContent] 参数必须是 string 或 ref string");
                }
                map[i] = new(ParameterRole.Content, null, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                contentFound = true;
            }
        }

        for (int i = 0; i < ps.Length; i++)
        {
            if (map[i] != null)
            {
                continue;
            }
            
            if (contentFound == false && (ps[i].ParameterType == typeof(string) || ps[i].ParameterType == typeof(string).MakeByRefType()))
            {
                map[i] = new(ParameterRole.Content, null, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                contentFound = true;
                continue;
            }
            map[i] = new(ParameterRole.Attribute, ps[i].Name, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
        }

        if (contentFound == false)
        {
            throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name}: 缺少内容参数");
        }
        return map!;
    }

    static object?[] ResolveArguments(ParameterMapping[] mappings, XmlTagContext ctx, string content, IReadOnlyDictionary<string, string> attrs)
    {
        object?[] args = new object?[mappings.Length];
        for (int i = 0; i < mappings.Length; i++)
        {
            args[i] = mappings[i].Role switch
            {
                ParameterRole.Context => ctx,
                ParameterRole.Content => content,
                ParameterRole.Attribute => ResolveAttr(mappings[i], attrs),
                _ => null
            };
        }
        return args;
    }

    static object? ResolveAttr(ParameterMapping m, IReadOnlyDictionary<string, string> attrs)
    {
        if (attrs.TryGetValue(m.AttributeName!, out string? raw))
        {
            return ConvertValue(raw, m.ParameterType);
        }
        
        if (m.HasDefaultValue)
        {
            return m.DefaultValue;
        }
        
        return m.ParameterType.IsValueType ? Activator.CreateInstance(m.ParameterType) : null;
    }

    static object? ConvertValue(string value, Type t)
    {
        if (t == typeof(string))
        {
            return value;
        }
        
        Type? u = Nullable.GetUnderlyingType(t);
        if (u != null)
        {
            return string.IsNullOrEmpty(value) ? null : Convert.ChangeType(value, u);
        }
        
        if (t.IsEnum)
        {
            return Enum.Parse(t, value, true);
        }
        
        if (t == typeof(bool))
        {
            return value is "1" or "true" or "True" or "TRUE";
        }
        
        return Convert.ChangeType(value, t);
    }
}
