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
            // 优先使用 DescriptionAttribute (KernelFunction 风格)，若为空则回退
            var descAttr = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            string effectiveDescription = (descAttr != null && !string.IsNullOrEmpty(descAttr.Description)) 
                ? descAttr.Description 
                : description;
            
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
                    
                    var pDescAttr = ps[i].GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
                    string pDesc = (pDescAttr != null && !string.IsNullOrEmpty(pDescAttr.Description))
                        ? pDescAttr.Description
                        : "";
                    
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
                
                // 回填 ref string (只有当参数确实是 ref 类型，且被标记为 Content Role 时才允许回填到流内容中)
                for (int j = 0; j < mappings.Length; j++)
                {
                    if (mappings[j].Role == ParameterRole.Content && mappings[j].ParameterType.IsByRef && args[j] is string newContent)
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
                map[i] = new(ParameterRole.Content, ps[i].Name, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                contentFound = true;
            }
        }

        for (int i = 0; i < ps.Length; i++)
        {
            if (map[i] != null) continue;
            
            // 自动识别 Content 角色：首个 string 参数
            if (contentFound == false && (ps[i].ParameterType == typeof(string) || ps[i].ParameterType == typeof(string).MakeByRefType()))
            {
                map[i] = new(ParameterRole.Content, ps[i].Name, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                contentFound = true;
                continue;
            }

            // 其余为属性
            map[i] = new(ParameterRole.Attribute, ps[i].Name, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
        }

        // 注意：内容参数现在是可选的。如果没有找到 string 参数，处理器只是无法接收标签文本内容。
        return map!;
    }

    static object?[] ResolveArguments(ParameterMapping[] mappings, XmlTagContext ctx, string content, IReadOnlyDictionary<string, string> attrs)
    {
        object?[] args = new object?[mappings.Length];

        // Rule 1: 先给 Context 和 Content 赋值
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].Role == ParameterRole.Context)
            {
                args[i] = ctx;
            }
            else if (mappings[i].Role == ParameterRole.Content)
            {
                args[i] = content;
            }
        }

        // Rule 2: 属性赋值与覆盖 (仅针对 Attribute 角色，内容参数不再被属性覆盖)
        for (int i = 0; i < mappings.Length; i++)
        {
            var m = mappings[i];
            if (m.Role != ParameterRole.Attribute) continue;

            string? attrKey = m.AttributeName;
            string? attrValue = null;
            if (attrKey != null && attrs.TryGetValue(attrKey, out attrValue))
            {
                // 尝试转换属性值
                object? converted = ConvertValue(attrValue!, m.ParameterType);
                if (converted != null || m.ParameterType.IsPointer || (Nullable.GetUnderlyingType(m.ParameterType) != null && string.IsNullOrEmpty(attrValue)))
                {
                    args[i] = converted;
                }
            }
        }

        // Rule 3: 默认值回退 (针对所有非 Context 参数，且尚未赋值的)
        for (int i = 0; i < mappings.Length; i++)
        {
            var m = mappings[i];
            if (m.Role == ParameterRole.Context) continue;
            if (args[i] != null) continue;

            if (m.HasDefaultValue)
            {
                args[i] = m.DefaultValue;
            }
            else
            {
                // 无默认值时的 fallback
                args[i] = m.ParameterType.IsValueType ? Activator.CreateInstance(m.ParameterType) : null;
            }
        }

        return args;
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
