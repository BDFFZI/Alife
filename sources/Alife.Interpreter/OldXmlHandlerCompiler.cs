using System.Reflection;
using System.ComponentModel;

namespace Alife.Interpreter;

public class XmlHandlerCompiler
{
    public XmlHandlerCompiler Register(object handlerInstance)
    {
        Type type = handlerInstance.GetType();
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<XmlFunctionAttribute>() is not { } attr) continue;
            registrations.Add(new HandlerRegistration(
                method.IsStatic ? null : handlerInstance, method, attr.Name, attr.Description, type
            ));
        }
        return this;
    }
    public XmlHandlerCompiler Register<T>() where T : class
    {
        foreach (MethodInfo method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<XmlFunctionAttribute>() is not { } attr) continue;
            registrations.Add(new HandlerRegistration(
                null, method, attr.Name, attr.Description, typeof(T)
            ));
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

        foreach (HandlerRegistration reg in registrations)
        {
            ParameterMapping[] mappings = BuildParameterMappings(reg.Method);
            object? capturedTarget = reg.Target;
            MethodInfo capturedMethod = reg.Method;

            CompiledTagInvoker invoker = (XmlTagContext ctx, ref string content, IReadOnlyDictionary<string, string> attrs) => {
                object?[] args = ResolveArguments(mappings, ctx, content, attrs);
                object? result = capturedMethod.Invoke(capturedTarget, args);

                for (int j = 0; j < mappings.Length; j++)
                {
                    if (mappings[j].Role != ParameterRole.Content || mappings[j].ParameterType.IsByRef == false || args[j] is not string newContent)
                        continue;

                    content = newContent;
                    break;
                }

                return result as Task ?? Task.CompletedTask;
            };

            if (reg.TagName == null)
            {
                catchAllHandlers.Add(invoker);
                continue;
            }

            string effectiveTagName = reg.TagName.ToLowerInvariant();
            string className = reg.DeclaringType.Name;

            if (classDescriptions.ContainsKey(className) == false)
            {
                classDescriptions[className] = declaringType.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            }

            tagToClass[effectiveTagName] = className;

            DescriptionAttribute? descAttr = reg.Method.GetCustomAttribute<DescriptionAttribute>();
            descriptions[effectiveTagName] = (descAttr != null && string.IsNullOrEmpty(descAttr.Description) == false)
                ? descAttr.Description
                : reg.Description;

            tagParameters[effectiveTagName] = BuildParameterInfos(reg.Method, mappings);

            if (handlers.TryGetValue(effectiveTagName, out CompiledTagInvoker? existing))
            {
                CompiledTagInvoker prev = existing;
                handlers[effectiveTagName] = (XmlTagContext ctx, ref string c, IReadOnlyDictionary<string, string> a) => {
                    return Task.WhenAll(prev(ctx, ref c, a), invoker(ctx, ref c, a));
                };
            }
            else
            {
                handlers[effectiveTagName] = invoker;
            }
        }

        return new XmlHandlerTable(handlers, catchAllHandlers, descriptions, tagParameters, classDescriptions, tagToClass);
    }

    enum ParameterRole
    {
        Context,
        Content,
        Attribute
    }

    sealed record ParameterMapping(
        ParameterRole Role,
        string? AttributeName,
        Type ParameterType,
        bool HasDefaultValue,
        object? DefaultValue);

    record struct HandlerRegistration(
        object? Target,
        MethodInfo Method,
        string? TagName,
        string Description,
        Type DeclaringType);

    readonly List<HandlerRegistration> registrations = new();

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

            DescriptionAttribute? pDescAttr = ps[i].GetCustomAttribute<DescriptionAttribute>();
            string pDesc = pDescAttr != null && string.IsNullOrEmpty(pDescAttr.Description) == false ? pDescAttr.Description : "";

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

        if (Nullable.GetUnderlyingType(t) is { } u) return GetAiTypeName(u);

        return t.Name;
    }

    /// <summary>
    /// 构建参数映射表
    /// </summary>
    static ParameterMapping[] BuildParameterMappings(MethodInfo method)
    {
        ParameterInfo[] ps = method.GetParameters();
        ParameterMapping?[] map = new ParameterMapping?[ps.Length];
        bool contentFound = false;

        //确认XmlTagContext和XmlTagContent参数
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
            if (ps[i].ParameterType == typeof(XmlTagContext))
            {
                map[i] = new(ParameterRole.Context, null, ps[i].ParameterType, ps[i].HasDefaultValue, ps[i].DefaultValue);
                continue;
            }
            
            if (map[i] != null) continue;

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
            ParameterMapping m = mappings[i];

            if (m.Role == ParameterRole.Context)
            {
                args[i] = ctx;
                continue;
            }

            if (m.Role == ParameterRole.Content)
            {
                args[i] = content;
                continue;
            }

            if (m.AttributeName != null && attrs.TryGetValue(m.AttributeName, out string? attrValue))
            {
                object? converted = ConvertValue(attrValue, m.ParameterType);
                if (converted != null || m.ParameterType.IsPointer || (Nullable.GetUnderlyingType(m.ParameterType) != null && string.IsNullOrEmpty(attrValue)))
                {
                    args[i] = converted;
                    continue;
                }
            }

            if (m.HasDefaultValue)
                args[i] = m.DefaultValue;
            else
                args[i] = m.ParameterType.IsValueType ? Activator.CreateInstance(m.ParameterType) : null;
        }

        return args;
    }

    static object? ConvertValue(string value, Type t)
    {
        if (t == typeof(string)) return value;

        if (Nullable.GetUnderlyingType(t) is { } u)
            return string.IsNullOrEmpty(value) ? null : Convert.ChangeType(value, u);

        if (t.IsEnum) return Enum.Parse(t, value, true);
        if (t == typeof(bool)) return value is "1" or "true" or "True" or "TRUE";

        return Convert.ChangeType(value, t);
    }
}
