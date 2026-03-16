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

    readonly List<(object? Target, MethodInfo Method, string? TagName, string Description)> registrations = new();

    public XmlHandlerCompiler Register(object handlerInstance)
    {
        Type type = handlerInstance.GetType();
        foreach (MethodInfo method in type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            XmlHandlerAttribute? attr = method.GetCustomAttribute<XmlHandlerAttribute>();
            if (attr != null)
            {
                registrations.Add((method.IsStatic ? null : handlerInstance, method, attr.TagName, attr.Description));
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
                registrations.Add((null, method, attr.TagName, attr.Description));
            }
        }
        return this;
    }

    public XmlHandlerTable Compile()
    {
        Dictionary<string, CompiledTagInvoker> handlers = new();
        Dictionary<string, string> descriptions = new();
        Dictionary<string, List<XmlParameterInfo>> tagParameters = new();

        foreach ((object? target, MethodInfo method, string? tagName, string description) in registrations)
        {
            string effectiveTagName = (tagName ?? method.Name).ToLowerInvariant();
            string effectiveDescription = description;
            
            if (string.IsNullOrEmpty(effectiveDescription))
            {
                effectiveDescription = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? "";
            }

            descriptions[effectiveTagName] = effectiveDescription;
            ParameterMapping[] mappings = BuildParameterMappings(method);
            
            List<XmlParameterInfo> paramInfos = new();
            ParameterInfo[] ps = method.GetParameters();
            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping.Role == ParameterRole.Attribute || mapping.Role == ParameterRole.Content)
                {
                    Type type = mapping.ParameterType;
                    string typeName = GetAITypeName(type);
                    string[]? possibleValues = type.IsEnum ? Enum.GetNames(type) : null;
                    
                    string pDesc = ps[i].GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? "";
                    
                    paramInfos.Add(new XmlParameterInfo(
                        mapping.AttributeName ?? ps[i].Name ?? "content", 
                        typeName, 
                        mapping.HasDefaultValue, 
                        pDesc, 
                        possibleValues,
                        mapping.Role == ParameterRole.Content));
                }
            }
            tagParameters[effectiveTagName] = paramInfos;

            object? tObj = target; MethodInfo mInfo = method;
            
            CompiledTagInvoker invoker = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
            {
                object?[] args = ResolveArguments(mappings, ctx, content, attrs);
                
                object? result = mInfo.Invoke(tObj, args);
                
                // 回填 ref string (因为 Invoke 会更新 args 数组中的对象)
                for (int j = 0; j < mappings.Length; j++)
                {
                    if (mappings[j].Role == ParameterRole.Content && args[j] is string newContent)
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

            if (handlers.TryGetValue(effectiveTagName, out CompiledTagInvoker? existing))
            {
                CompiledTagInvoker prev = existing;
                handlers[effectiveTagName] = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
                {
                    Task t1 = prev(ctx, ref content, attrs);
                    Task t2 = invoker(ctx, ref content, attrs);
                    return Task.WhenAll(t1, t2);
                };
                continue;
            }
            
            handlers[effectiveTagName] = invoker;
        }
        return new XmlHandlerTable(handlers, descriptions, tagParameters);
    }

    static string GetAITypeName(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "int";
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t.IsEnum) return "enum{" + string.Join(",", Enum.GetNames(t)) + "}";
        
        Type? u = Nullable.GetUnderlyingType(t);
        if (u != null) return GetAITypeName(u);

        return t.Name;
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
        try
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
        catch
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}
