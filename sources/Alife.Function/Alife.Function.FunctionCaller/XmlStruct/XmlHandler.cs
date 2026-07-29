using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Interpreter;

public class XmlHandler
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public string? Explanation { get; init; }
    public List<XmlFunction> Functions { get; init; } = new();

    public string FunctionDocument()
    {
        StringBuilder sb = new();
        foreach (XmlFunction function in Functions)
            sb.AppendLine(function.Document());
        return sb.ToString().Trim();
    }
    public string Document()
    {
        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"# {Name} 功能介绍");
        stringBuilder.AppendLine(Description);
        stringBuilder.AppendLine("## 提供函数");
        stringBuilder.AppendLine(FunctionDocument());
        if (string.IsNullOrEmpty(Explanation) == false)
        {
            stringBuilder.AppendLine("## 使用说明");
            stringBuilder.AppendLine(Explanation);
        }
        return stringBuilder.ToString();
    }

    public XmlHandler(string name = "")
    {
        Name = name;
    }
    public XmlHandler(object instance, string? explanation = null)
    {
        Type handlerType = instance.GetType();
        Name = handlerType.Name;
        Description = handlerType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        Explanation = explanation;

        foreach (MethodInfo method in handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            XmlFunction? function = ParseFunction(method, instance);
            if (function != null)
                Functions.Add(function);
        }
    }

    static XmlFunction? ParseFunction(MethodInfo method, object handler)
    {
        XmlFunctionAttribute? functionAttribute = method.GetCustomAttribute<XmlFunctionAttribute>();
        if (functionAttribute == null)
            return null;

        ParameterInfo[] rawParameters = method.GetParameters();
        //统计参数信息
        string? contentName = null;
        string? contentDescription = null;
        List<XmlParameter> normalParameters = new();
        foreach (ParameterInfo parameterInfo in rawParameters)
        {
            if (parameterInfo.Name == null)
                continue;

            string parameterName = parameterInfo.Name.ToLower();
            string? parameterDescription = parameterInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;

            //特殊参数
            if (parameterInfo.ParameterType.IsAssignableTo(typeof(XmlContext)) ||
                parameterInfo.ParameterType == typeof(CancellationToken))
                continue;

            //内容参数
            if (parameterInfo.GetCustomAttribute<XmlContentAttribute>() != null)
            {
                contentName = parameterName;
                contentDescription = parameterDescription;
                continue;
            }

            //普通参数
            if (TypeDescriptor.GetConverter(parameterInfo.ParameterType).CanConvertFrom(typeof(string)))
            {
                bool isCanNull = Nullable.GetUnderlyingType(parameterInfo.ParameterType) != null;
                Type parameterType = isCanNull ? parameterInfo.ParameterType.GenericTypeArguments[0] : parameterInfo.ParameterType;
                string parameterTypeName = parameterType.IsEnum ? string.Join(" | ", parameterType.GetEnumNames()) : parameterType.Name;
                if (parameterInfo.HasDefaultValue)
                    parameterTypeName += "[可选]";

                normalParameters.Add(new XmlParameter() {
                    Name = parameterName,
                    Description = parameterDescription,
                    Type = parameterTypeName,
                    IsXmlForm = parameterInfo.GetCustomAttribute<XmlFormAttribute>() != null
                });
                continue;
            }

            throw new NotSupportedException("不支持的参数类型！");
        }

        //函数参数缓冲区
        object?[] parameterValuesBuffer = new object?[rawParameters.Length];

        Task Invoker(XmlContext context, CancellationToken cancellationToken)
        {
            //验证调用模式
            if (context.CallMode == CallMode.Opening || context.CallMode == CallMode.Content || context.CallMode == CallMode.Closing)
            {
                if ((functionAttribute.Mode & FunctionMode.Content) == 0)
                    throw new Exception($"调用 {method.Name} 标签的方式错误，应该使用单个自闭合标签调用。");
            }
            else if (context.CallMode == CallMode.OneShot)
            {
                if ((functionAttribute.Mode & FunctionMode.OneShot) == 0)
                    throw new Exception($"调用 {method.Name} 标签的方式错误，应该使用两个开闭标签包裹调用。");
            }

            //填充函数参数
            for (int index = 0; index < rawParameters.Length; index++)
            {
                ParameterInfo parameterInfo = rawParameters[index];

                object? result = null;
                bool isFilled = false;

                //尝试特殊值
                {
                    if (parameterInfo.ParameterType.IsInstanceOfType(context))
                    {
                        result = context;
                        isFilled = true;
                    }
                    else if (parameterInfo.ParameterType == typeof(CancellationToken))
                    {
                        result = cancellationToken;
                        isFilled = true;
                    }
                    else if (parameterInfo.GetCustomAttribute<XmlContentAttribute>() != null)
                    {
                        result = context.Content;
                        isFilled = true;
                    }
                }

                //尝试输入值
                if (isFilled == false)
                {
                    TypeConverter converter = TypeDescriptor.GetConverter(parameterInfo.ParameterType);
                    if (converter.CanConvertFrom(typeof(string)))
                    {
                        //可以由字符串转换
                        if (parameterInfo.Name != null && context.Parameters.TryGetValue(parameterInfo.Name.ToLower(), out string? value))
                        {
                            //有传入的字符串参数
                            try
                            {
                                result = converter.ConvertFromInvariantString(value);
                                isFilled = true;
                            }
                            catch (Exception)
                            {
                                // 解析失败
                            }
                        }
                    }
                }

                //尝试默认值
                if (isFilled == false)
                {
                    object? defaultValue = rawParameters[index].DefaultValue;
                    if (defaultValue != DBNull.Value)
                    {
                        result = defaultValue;
                        isFilled = true;
                    }
                }

                if (isFilled)
                    parameterValuesBuffer[index] = result;
                else if (parameterInfo.GetCustomAttribute<XmlFormAttribute>() == null)
                    throw new Exception($"<{method.Name}> 缺少{parameterInfo.Name}参数，请检查是否遗漏，或传入的参数格式是否正确！");
            }

            //调用
            object? back = method.Invoke(handler, parameterValuesBuffer);

            //处理返回值
            if (back is Task task) return task;
            return Task.CompletedTask;
        }

        return new XmlFunction {
            Name = functionAttribute.Name ?? method.Name.ToLower(),
            Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
            ContentName = contentName ?? (functionAttribute.Mode != FunctionMode.Content ? null : normalParameters.Any(parameter => parameter.IsXmlForm) ? "" : "Content"),
            ContentDescription = contentDescription,
            Parameters = normalParameters,
            Order = functionAttribute.Order,
            Invoker = Invoker,
        };
    }
}
