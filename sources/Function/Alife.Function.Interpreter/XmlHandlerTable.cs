using Alife.Basic;
using System.ComponentModel;
using System.Reflection;

namespace Alife.Function.Interpreter;

[AttributeUsage(AttributeTargets.Method)]
public class XmlFunctionAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
[AttributeUsage(AttributeTargets.Parameter)]
public class XmlContentAttribute : Attribute { }
public class XmlContext
{
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public string Content { get; set; } = "";
}
public record struct XmlHandler
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public List<XmlFunction> Functions { get; init; }
    public object Instance { get; init; }
}
public record struct XmlFunction
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public string? ContentName { get; init; }
    public string? ContentDescription { get; init; }
    public List<XmlParameter> Parameters { get; init; }
    public Func<XmlContext, Task> Invoker { get; init; }
}
public record struct XmlParameter
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public string Type { get; init; }
}
public class XmlHandlerTable
{
    public void Register(object handler)
    {
        Type handlerType = handler.GetType();

        List<XmlFunction> functions = new();
        foreach (MethodInfo method in handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            XmlFunction? function = ParseFunction(method, handler);
            if (function == null)
                continue;
            functions.Add(function.Value);
        }

        DescriptionAttribute? descriptionAttribute = handlerType.GetCustomAttribute<DescriptionAttribute>();
        XmlHandler xmlHandler = new XmlHandler() {
            Name = handlerType.Name,
            Description = descriptionAttribute?.Description,
            Functions = functions,
            Instance = handler,
        };

        xmlHandlers.Add(xmlHandler);

        foreach (XmlFunction xmlFunction in functions)
        {
            if (xmlInvokers.TryGetValue(xmlFunction.Name, out List<Func<XmlContext, Task>>? invokers) == false)
            {
                invokers = new List<Func<XmlContext, Task>>();
                xmlInvokers[xmlFunction.Name] = invokers;
            }

            invokers.Add(xmlFunction.Invoker);
        }
    }
    public string Document()
    {
        System.Text.StringBuilder sb = new();

        foreach (XmlHandler handler in xmlHandlers)
        {
            sb.AppendLine(handler.Name);
            if (string.IsNullOrEmpty(handler.Description) == false)
            {
                sb.AppendLine($"> {handler.Description}");
            }

            foreach (XmlFunction function in handler.Functions)
            {
                sb.Append($"- <{function.Name}");
                foreach (XmlParameter param in function.Parameters)
                {
                    string pDesc = string.IsNullOrEmpty(param.Description) ? "" : $"（{param.Description}）";
                    sb.Append($" {param.Name}=\"{param.Type}\"{pDesc}");
                }

                if (function.ContentName != null)
                {
                    sb.Append(">");
                    string cDesc = string.IsNullOrEmpty(function.ContentDescription) ? "" : $"（{function.ContentDescription}）";
                    sb.Append($"{function.ContentName}{cDesc}</{function.Name}>");
                }
                else
                {
                    sb.Append(" />");
                }

                if (string.IsNullOrEmpty(function.Description) == false)
                    sb.Append($" : {function.Description}");

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
    public async Task TryHandle(string name, XmlContext tagContext)
    {
        if (xmlInvokers.TryGetValue(name, out List<Func<XmlContext, Task>>? invokers) == false)
            return;

        try
        {
            await Task.WhenAll(invokers.Select(func => func.Invoke(tagContext)));
        }
        catch (Exception e)
        {
            Terminal.LogError(e.ToString());
        }
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, List<Func<XmlContext, Task>>> xmlInvokers = new();

    XmlFunction? ParseFunction(MethodInfo method, object handler)
    {
        XmlFunctionAttribute? functionAttribute = method.GetCustomAttribute<XmlFunctionAttribute>();
        if (functionAttribute == null)
            return null;

        string name = functionAttribute.Name ?? method.Name.ToLower();
        string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;

        ParameterInfo[] rawParameters = method.GetParameters();
        //统计参数信息
        int contextParameterIndex = -1;
        int contentParameterIndex = -1;
        Dictionary<string, int> normalParameterIndices = new();
        List<XmlParameter> normalParameters = new();
        string? contentName = null;
        string? contentDescription = null;
        for (int index = 0; index < rawParameters.Length; index++)
        {
            ParameterInfo parameterInfo = rawParameters[index];
            if (parameterInfo.Name == null)
                continue;

            string parameterName = parameterInfo.Name.ToLower();
            string? parameterDescription = parameterInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;

            if (parameterInfo.ParameterType.IsAssignableTo(typeof(XmlContext)))
            {
                contextParameterIndex = index;
            }
            else if (parameterInfo.ParameterType == typeof(string).MakeByRefType() || parameterInfo.GetCustomAttribute<XmlContentAttribute>() != null)
            {
                contentParameterIndex = index;
                contentName = parameterName;
                contentDescription = parameterDescription;
            }
            else if (TypeDescriptor.GetConverter(parameterInfo.ParameterType).CanConvertFrom(typeof(string)))
            {
                string parameterType = parameterInfo.ParameterType.Name;
                if (parameterInfo.ParameterType.IsEnum)
                    parameterType = string.Join(" | ", parameterInfo.ParameterType.GetEnumNames());

                normalParameters.Add(new XmlParameter() {
                    Name = parameterName,
                    Description = parameterDescription,
                    Type = parameterType,
                });
                normalParameterIndices[parameterName] = index;
            }
            else
            {
                throw new NotSupportedException("不支持的参数类型！");
            }
        }

        //统计调用方法
        object?[] parameterValuesBuffer = new object?[rawParameters.Length];
        Task Invoker(XmlContext context)
        {
            //填充默认值
            for (int index = 0; index < rawParameters.Length; index++)
            {
                object? defaultValue = rawParameters[index].DefaultValue;
                if (defaultValue == null || defaultValue == DBNull.Value)
                {
                    Type parameterType = rawParameters[index].ParameterType;
                    defaultValue = parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
                }
                parameterValuesBuffer[index] = defaultValue;
            }
            //接收输入值
            foreach ((string name, string value) in context.Parameters)
            {
                if (normalParameterIndices.TryGetValue(name, out int index) == false)
                    continue; //没有同名参数
                TypeConverter converter = TypeDescriptor.GetConverter(rawParameters[index].ParameterType);
                if (converter.CanConvertFrom(typeof(string)) == false)
                    continue; //无法通过字符串转换

                try
                {
                    object? result = converter.ConvertFromInvariantString(value);
                    parameterValuesBuffer[index] = result;
                }
                catch (Exception)
                {
                    // Console.WriteLine(e);
                }
            }
            //设置特殊值
            if (contextParameterIndex != -1 && rawParameters[contextParameterIndex].ParameterType.IsInstanceOfType(context))
                parameterValuesBuffer[contextParameterIndex] = context;
            if (contentParameterIndex != -1)
                parameterValuesBuffer[contentParameterIndex] = context.Content;

            //调用
            object? back = method.Invoke(handler, parameterValuesBuffer);

            //处理返回值
            if (contentParameterIndex != -1) context.Content = parameterValuesBuffer[contentParameterIndex] as string ?? "";
            if (back is Task task) return task;
            return Task.CompletedTask;
        }

        return new XmlFunction() {
            Name = name,
            Description = description,
            ContentName = contentName,
            ContentDescription = contentDescription,
            Parameters = normalParameters,
            Invoker = Invoker,
        };
    }
}
