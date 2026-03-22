# Alife.Interpreter

一个轻量级、高性能的流式 XML 解释器，专为大语言模型（LLM）的流式输出交互而设计。

## 核心特性

- **流式解析 (Streaming)**: 基于状态机的逐字符解析，能够实时处理 LLM 生成的文本流。
- **顺序冒泡 (Sequential Bubbling)**: 采用由内向外的冒泡执行机制。内层标签可以截断、修改或通过文本，外层标签接收处理后的结果。
- **智能参数解析**:
    - **优先级策略**: 遵循 `内容赋值 > 属性覆盖 > 默认值回退`。
    - **内容保护**: 标记为“内容”角色的参数禁止被同名标签属性覆盖，确保流式文本的完整性。
- **AI 友好型文档**: 自动从 C# 代码（支持 `DescriptionAttribute`）生成 AI 可读的接口文档。
- **自动断句**: 灵活的可配置分词器（如 `。`、`！`），在流式传输过程中根据句长自动触发处理器。

## 架构组成

- **XmlStreamParser**: 纯粹的 XML 状态机解析器，负责识别开标签、属性、文本和闭标签。
- **XmlStreamExecutor**: 核心执行引擎。维护标签栈、处理缓冲区、执行断句逻辑并调度冒泡调用链。
- **XmlHandlerCompiler**: 编译器。通过反射将带有 `[XmlHandler]` 特性的 C# 方法编译为可执行的委托。
- **XmlHandlerTable**: 存储编译后的映射表，提供接口文档生成功能。
- **XmlTagContext**: 执行上下文。为处理器提供当前的调用链信息、触发状态（Opening/Content/Closing）以及完整内容。

## 核心逻辑详解

### 1. 顺序冒泡与中断机制
当流式解析器检测到一段有效内容或标签关闭时，`XmlStreamExecutor` 会启动执行链：
- 调用顺序：`最内层标签处理器 -> ... -> 最外层标签处理器`。
- 处理器可以使用 `ref string content`。
- **中断规则**：如果内层处理器将内容清空，后续外层处理器的 `Content` 步调用将被跳过（除非是该处理器的 `Closing` 事件）。

### 2. 参数分发三原则
1. **Rule 1 (内容)**: 标签内的文本首先分配给首个 `string` 类型参数。
2. **Rule 2 (属性)**: 尝试用标签属性覆盖同名参数。**注意**：被 Rule 1 占用的参数受保护，不会被覆盖。
3. **Rule 3 (默认值)**: 缺失的参数使用 C# 零值或定义时的默认值。

### 3. 断句逻辑
通过 `minResultLength` 和 `sentenceBreakers` 配置。解释器会在缓冲区满足最小长度且检测到断句符时，提前触发一轮 `Content` 冒泡，实现“边生成边执行”的效果。

## 快速上手

```csharp
// 1. 定义处理器
public class MyHandlers {
    [XmlHandler(description: "向用户说话")]
    public void Say(XmlTagContext ctx, [Description("消息内容")] string message) {
        Console.WriteLine($"AI 说: {message}");
    }
}

// 2. 编译并启动
var handlerTable = new XmlHandlerCompiler().Register(new MyHandlers()).Compile();
var executor = new XmlStreamExecutor(new XmlStreamParser(), handlerTable);

// 3. 喂入流式数据
await executor.FeedAsync("<say>你好啊，这是流式内容");
await executor.FeedAsync("。</say>");
```

---
*Created by Antigravity AI*
