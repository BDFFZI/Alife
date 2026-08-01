using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.FunctionCaller;

public class XmlHandlerTable
{
    public IReadOnlyList<XmlHandler> GetAllHandlers()
    {
        return xmlHandlers;
    }
    public IReadOnlyList<XmlHandler>? GetHandlersOfFunction(string functionName)
    {
        return functionToHandler.GetValueOrDefault(functionName);
    }

    public void Register(XmlHandler handler)
    {
        xmlHandlers.Add(handler);
        foreach (XmlFunction xmlFunction in handler.Functions)
        {
            //注册函数
            {
                if (xmlFunctions.TryGetValue(xmlFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup) == false)
                {
                    xmlFunctionGroup = new SortedSet<XmlFunction>();
                    xmlFunctions[xmlFunction.Name] = xmlFunctionGroup;
                }

                xmlFunctionGroup.Add(xmlFunction);
            }

            //注册函数到处理器映射
            {
                if (functionToHandler.TryGetValue(xmlFunction.Name, out List<XmlHandler>? xmlHandlerGroup) == false)
                {
                    xmlHandlerGroup = new List<XmlHandler>();
                    functionToHandler[xmlFunction.Name] = xmlHandlerGroup;
                }

                xmlHandlerGroup.Add(handler);
            }

            foreach (XmlParameter parameter in xmlFunction.Parameters)
            {
                if (parameter.IsXmlForm)
                    xmlForms.Add(parameter.Name.ToLower());
            }
        }
    }
    public void Unregister(XmlHandler handler)
    {
        xmlHandlers.Remove(handler);
        foreach (XmlFunction xmlHandlerFunction in handler.Functions)
        {
            if (xmlFunctions.TryGetValue(xmlHandlerFunction.Name, out SortedSet<XmlFunction>? xmlFunctionGroup))
                xmlFunctionGroup.Remove(xmlHandlerFunction);
        }
    }

    /// <summary>
    /// 取消被禁用的函数
    /// </summary>
    /// <param name="function"></param>
    public void EnableFunction(XmlFunction function)
    {
        functions.Remove(function);
    }
    /// <summary>
    /// 被禁用的函数将被跳过执行
    /// </summary>
    /// <param name="function"></param>
    public void DisableFunction(XmlFunction function)
    {
        functions.Add(function);
    }

    public async Task Handle(string name, XmlContext tagContext, CancellationToken cancellationToken = default)
    {
        name = name.ToLower();
        SortedSet<XmlFunction>? xmlFunctionGroup = xmlFunctions.GetValueOrDefault(name);
        if (xmlFunctionGroup == null || xmlFunctionGroup.Count == 0)
        {
            if (xmlForms.Contains(name))
                return;
            throw new Exception($"环境中没有<{name}/>，请停止使用");
        }
        foreach (XmlFunction xmlFunction in xmlFunctionGroup)
        {
            if (functions.Contains(xmlFunction))
                continue;
            await xmlFunction.Invoker(tagContext, cancellationToken);
        }
    }

    readonly List<XmlHandler> xmlHandlers = new();
    readonly Dictionary<string, SortedSet<XmlFunction>> xmlFunctions = new();
    readonly Dictionary<string, List<XmlHandler>> functionToHandler = new();
    readonly HashSet<XmlFunction> functions = new();
    readonly HashSet<string> xmlForms = new();
}
