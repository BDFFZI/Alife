using System.ComponentModel;
using System.Reflection;
using Alife.Interpreter;

public record struct XmlHandler
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public List<XmlFunction> Functions { get; init; }
}

public record struct XmlFunction
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public List<XmlParameter> Parameters { get; init; }
    public Func<XmlTagContext, Task> Invoker { get; init; }
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
            Functions = functions
        };

        xmlHandlers.Add(xmlHandler);

        foreach (XmlFunction xmlFunction in functions)
            xmlInvokers[xmlFunction.Name] += xmlFunction.Invoker;
    }
    public string Document()
    {
        
    }
    public void Handle(XmlTagContext tagContext)
    {
        Func<XmlTagContext, Task> invokers = xmlInvokers[tagContext.CallChain.Last()];

        Task.WaitAll(invokers.GetInvocationList()
            .Cast<Func<XmlTagContext, Task>>()
            .Select(func => func.Invoke(tagContext)));

        foreach (Delegate @delegate in invokers.GetInvocationList())
        {
            Func<XmlTagContext, Task> invoker = (Func<XmlTagContext, Task>)@delegate;
            invoker.Invoke(tagContext);
        }
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, Func<XmlTagContext, Task>> xmlInvokers = new();

    XmlFunction? ParseFunction(MethodInfo method, object handler)
    {
        XmlFunctionAttribute? functionAttribute = method.GetCustomAttribute<XmlFunctionAttribute>();
        if (functionAttribute == null)
            return null;

        string name = functionAttribute.Name ?? method.Name;
        string? description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;

        ParameterInfo[] rawParameters = method.GetParameters();
        //统计参数信息
        int contextParameterIndex = -1;
        int contentParameterIndex = -1;
        Dictionary<string, int> normalParameterIndices = new();
        List<XmlParameter> normalParameters = new();
        for (int index = 0; index < rawParameters.Length; index++)
        {
            ParameterInfo parameterInfo = rawParameters[index];
            if (parameterInfo.Name == null)
                continue;

            if (parameterInfo.ParameterType == typeof(XmlTagContext))
            {
                contextParameterIndex = index;
            }
            else if (parameterInfo.ParameterType == typeof(string) && parameterInfo.ParameterType.IsByRef)
            {
                contentParameterIndex = index;
            }
            else
            {
                //参数信息
                string parameterName = parameterInfo.Name.ToLower();
                string? parameterDescription = parameterInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;
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
        }

        //统计调用方法
        object?[] parameterValuesBuffer = new object?[rawParameters.Length];
        Task Invoker(XmlTagContext context)
        {
            //填充默认值
            for (int index = 0; index < rawParameters.Length; index++) parameterValuesBuffer[index] = rawParameters[index].DefaultValue;
            //接收输入值
            foreach ((string name, string value) in context.CallParams)
            {
                if (normalParameterIndices.TryGetValue(name, out int index) == false)
                    continue; //没有同名参数
                TypeConverter converter = TypeDescriptor.GetConverter(rawParameters[index].ParameterType);
                if (converter.CanConvertFrom(typeof(string)) == false)
                    continue; //无法通过字符串转换

                parameterValuesBuffer[index] = converter.ConvertFromInvariantString(value);
            }
            //设置特殊值
            if (contextParameterIndex != -1) parameterValuesBuffer[contextParameterIndex] = context;
            if (contentParameterIndex != -1) parameterValuesBuffer[contentParameterIndex] = context.ChipContent;

            //调用
            object? result = method.Invoke(handler, parameterValuesBuffer);

            //处理返回值
            if (contentParameterIndex != -1) context.ChipContent = parameterValuesBuffer[contentParameterIndex] as string ?? "";
            if (result is Task task) return task;
            return Task.CompletedTask;
        }

        return new XmlFunction() {
            Name = name,
            Description = description,
            Parameters = normalParameters,
            Invoker = Invoker,
        };
    }
}
