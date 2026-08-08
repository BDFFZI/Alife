using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Foundation;

namespace Alife.Function.FileService;

[Module("文件操作",
    "提供文件读写、编辑、搜索能力",
    defaultCategory: "Alife 官方/实用工具")]
public class FileService(
    XmlFunctionCaller functionCaller,
    IInteractor<FileService> interactor) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.Content)]
    [Description("创建或覆盖一个文件")]
    public async Task Write(XmlExecutorContext context, string filePath, CancellationToken cancellationToken = default)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        await impl.WriteAsync(filePath, context.FullContent, cancellationToken);
        interactor.Poke($"文件已写入: {filePath}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("读取目录内容（不含子目录）或文件内容（返回格式为`行号: 内容`）")]
    public async Task Read(
        [Description("目录或文件路径")] string path,
        int? startLine = null,
        int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        FileReadResult result = await impl.ReadAsync(path, startLine, lineCount, cancellationToken);

        if (result.Error != null)
            throw new Exception(result.Error);

        if (result.TempFilePath != null)
            interactor.Poke($"内容过大，已写入临时文件: {result.TempFilePath}");
        else
            interactor.Poke(result.Content!);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("精确替换文件中的指定文本")]
    public async Task Edit(XmlExecutorContext context,
        string filePath,
        [XmlForm] string? oldString,
        [XmlForm] string? newString,
        [Description("是否替换所有匹配项")] bool replaceAll = false)
    {
        if (context.CallMode == CallMode.Closing)
        {
            if (string.IsNullOrEmpty(oldString) || string.IsNullOrEmpty(newString))
                throw new Exception("未提供 oldString 或 newString 标签内容");

            await impl.EditAsync(filePath, oldString, newString, replaceAll);
            interactor.Poke("文件已更新");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("按Glob通配符搜索文件")]
    public void Glob(string path, string pattern)
    {
        string[] files = impl.Glob(path, pattern);
        if (files.Length == 0)
            interactor.Poke("没有匹配的文件");
        else
            interactor.Poke(string.Join("\n", files));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("使用正则表达式搜索目录下的文件内容")]
    public void Grep(
        string directory,
        string pattern,
        [Description("文件过滤（如 *.cs）")] string? include = null)
    {
        GrepResult result = impl.Grep(directory, pattern, include);

        string output = result.Matches.Count > 0
            ? string.Join("\n", result.Matches) + (result.Truncated ? "\n...(结果已截断)" : "")
            : "未找到匹配结果";

        interactor.Poke(output);
    }

    readonly FileServiceImpl impl = new() {
        TempFolderPath = AlifePath.TempFolderPath
    };

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "当你需要更方便快捷的查找编辑文件时使用，例如用于编程工作"
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);
        functionCaller.AddPlainAreas(nameof(Write));

        return Task.CompletedTask;
    }
}