using System.Reflection;

namespace Alife.Interpreter;

public class XmlHandlerCompiler
{
    public XmlHandlerCompiler Register(object handlerInstance)
    {
        Type type = handlerInstance.GetType();
        foreach (MethodInfo method in type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            XmlHandlerAttribute? attr = method.GetCustomAttribute<XmlHandlerAttribute>();
            if (attr != null)
            {
                registrations.Add((method.IsStatic ? null : handlerInstance, method, attr.TagName, attr.Description, type));
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
                registrations.Add((null, method, attr.TagName, attr.Description, typeof(T)));
            }
        }
        return this;
    }

    public XmlHandlerTable Compile()
    {
        Dictionary<string, CompiledTagInvoker> handlers = new();
        List<CompiledTagInvoker> catchAllHandlers = new();
        Dictionary<string, string> descriptions = new();
        Dictionary<string, List<XmlParameterInfo>> tagParameters = new();
        Dictionary<string, string> classDescriptions = new();
        Dictionary<string, string> tagToClass = new();

        foreach ((object? target, MethodInfo method, string? tagName, string description, Type declaringType) in registrations)
        {
            ParameterMapping[] mappings = BuildParameterMappings(method);
            object? capturedTarget = target;
            MethodInfo capturedMethod = method;

            CompiledTagInvoker invoker = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
            {
                object?[] args = ResolveArguments(mappings, ctx, content, attrs);
                object? result = capturedMethod.Invoke(capturedTarget, args);

                // 回填 ref string（只有 Content 角色且为 ref 类型时才回填到流内容中）
                for (int j = 0; j < mappings.Length; j++)
                {
                    if (mappings[j].Role == ParameterRole.Content && mappings[j].ParameterType.IsByRef && args[j] is string newContent)
                    {
                        content = newContent;
                        break;
                    }
                }

                return result is Task task ? task : Task.CompletedTask;
            };

            // 通配处理器：tagName 为 null 时接收所有标签事件
            if (tagName == null)
            {
                catchAllHandlers.Add(invoker);
                continue;
            }

            string effectiveTagName = tagName.ToLowerInvariant();

            // 处理类说明
            string className = declaringType.Name;
            if (classDescriptions.ContainsKey(className) == false)
            {
                System.ComponentModel.DescriptionAttribute? classDescAttr = declaringType.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
                classDescriptions[className] = classDescAttr?.Description ?? "";
            }
            tagToClass[effectiveTagName] = className;

            // 描述：优先 DescriptionAttribute，回退到 XmlHandler 的 description
            System.ComponentModel.DescriptionAttribute? descAttr = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            string effectiveDescription = (descAttr != null && string.IsNullOrEmpty(descAttr.Description) == false)
                ? descAttr.Description
                : description;
            descriptions[effectiveTagName] = effectiveDescription;

            // 参数信息（用于文档生成）
            List<XmlParameterInfo> paramInfos = BuildParameterInfos(method, mappings);
            tagParameters[effectiveTagName] = paramInfos;

            // 同名标签合并（多个处理器并行执行）
            if (handlers.TryGetValue(effectiveTagName, out CompiledTagInvoker? existing))
            {
                CompiledTagInvoker prev = existing;
                handlers[effectiveTagName] = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) =>
                {
                    Task t1 = prev(ctx, ref content, attrs);
                    Task t2 = invoker(ctx, ref content, attrs);
                    return Task.WhenAll(t1, t2);
                };
            }
            else
            {
                handlers[effectiveTagName] = invoker;
            }
        }

        return new XmlHandlerTable(handlers, catchAllHandlers, descriptions, tagParameters, classDescriptions, tagToClass);
    }

    enum ParameterRole { Context, Content, Attribute }

    sealed record ParameterMapping(
        ParameterRole Role, string? AttributeName, Type ParameterType,
        bool HasDefaultValue, object? DefaultValue);

    readonly List<(object? Target, MethodInfo Method, string? TagName, string Description, Type DeclaringType)> registrations = new();

    static List<XmlParameterInfo> BuildParameterInfos(MethodInfo method, ParameterMapping[] mappings)
    {
        List<XmlParameterInfo> paramInfos = new();
        ParameterInfo[] ps = method.GetParameters();

        for (int i = 0; i < mappings.Length; i++)
        {
            ParameterMapping mapping = mappings[i];
            if (mapping.Role != ParameterRole.Attribute && mapping.Role != ParameterRole.Content)
                continue;

            Type type = mapping.ParameterType;
            string typeName = GetAiTypeName(type);
            string[]? possibleValues = type.IsEnum ? Enum.GetNames(type) : null;

            System.ComponentModel.DescriptionAttribute? pDescAttr = ps[i].GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            string pDesc = (pDescAttr != null && string.IsNullOrEmpty(pDescAttr.Description) == false)
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
        return paramInfos;
    }

    static string GetAiTypeName(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "int";
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t.IsEnum) return "enum{" + string.Join(",", Enum.GetNames(t)) + "}";

        Type? u = Nullable.GetUnderlyingType(t);
        if (u != null) return GetAiTypeName(u);

        return t.Name;
    }

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
                    throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name}: [XmlTagContent] 参数必须是 string 或 ref string");

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

            map[i] = new(ParameterRole.Attribute, ps[i].Name, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
        }

        return map!;
    }

    static object?[] ResolveArguments(ParameterMapping[] mappings, XmlTagContext ctx, string content, IReadOnlyDictionary<string, string> attrs)
    {
        object?[] args = new object?[mappings.Length];

        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].Role == ParameterRole.Context)
                args[i] = ctx;
            else if (mappings[i].Role == ParameterRole.Content)
                args[i] = content;
        }

        for (int i = 0; i < mappings.Length; i++)
        {
            ParameterMapping m = mappings[i];
            if (m.Role != ParameterRole.Attribute) continue;

            if (m.AttributeName != null && attrs.TryGetValue(m.AttributeName, out string? attrValue))
            {
                object? converted = ConvertValue(attrValue!, m.ParameterType);
                if (converted != null || m.ParameterType.IsPointer || (Nullable.GetUnderlyingType(m.ParameterType) != null && string.IsNullOrEmpty(attrValue)))
                {
                    args[i] = converted;
                }
            }
        }

        // 默认值回退
        for (int i = 0; i < mappings.Length; i++)
        {
            ParameterMapping m = mappings[i];
            if (m.Role == ParameterRole.Context) continue;
            if (args[i] != null) continue;

            if (m.HasDefaultValue)
                args[i] = m.DefaultValue;
            else
                args[i] = m.ParameterType.IsValueType ? Activator.CreateInstance(m.ParameterType) : null;
        }

        return args;
    }

    static object? ConvertValue(string value, Type t)
    {
        try
        {
            if (t == typeof(string)) return value;

            Type? u = Nullable.GetUnderlyingType(t);
            if (u != null)
                return string.IsNullOrEmpty(value) ? null : Convert.ChangeType(value, u);

            if (t.IsEnum) return Enum.Parse(t, value, true);
            if (t == typeof(bool)) return value is "1" or "true" or "True" or "TRUE";

            return Convert.ChangeType(value, t);
        }
        catch
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}
