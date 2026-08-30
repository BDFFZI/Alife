using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Alife.Foundation;

namespace Alife.Function.DeskPet;

#region Live2D官方规范扩展

public record ExpressionItem(string? Name);

public record MotionItem(string? Name, string? File);

public record FileReferencesModel
{
    public List<ExpressionItem> Expressions { get; set; } = new();
    public Dictionary<string, List<MotionItem>> Motions { get; set; } = new();
}

#endregion

#region 自定义的交互反馈

public record MotionRef(string Group, int Index);

public record InteractionItem(string? Text, string? Exp, MotionRef? Mtn);

#endregion

/// <summary>
/// Live2D 模型元数据载体与解析器
/// </summary>
public class PetModelMetadata
{
    public string ModelPath { get; private set; } = string.Empty;
    public List<string> Expressions { get; } = new();
    public Dictionary<string, (string Group, int Index)> Motions { get; } = new();
    public Dictionary<string, List<InteractionItem>> Interactions { get; } = new();

    /// <summary>从模型目录加载元数据（目录内按规范解析模型描述文件）。</summary>
    public static PetModelMetadata Load(string modelDirectory)
    {
        PetModelMetadata metadata = new();

        //按 Live2D 目录规范解析模型描述文件：目录名即模型名，定位 {目录名}.model3.json，缺省回退 .model.json。
        string modelJsonPath = ResolveModelJsonPath(modelDirectory);
        string ResolveModelJsonPath(string modelDirectory)
        {
            string modelName = Path.GetFileName(modelDirectory);
            string model3JsonPath = Path.Combine(modelDirectory, $"{modelName}.model3.json");
            if (File.Exists(model3JsonPath)) return model3JsonPath;

            string modelJsonPath = Path.Combine(modelDirectory, $"{modelName}.model.json");
            if (File.Exists(modelJsonPath)) return modelJsonPath;

            return model3JsonPath;
        }
        if (File.Exists(modelJsonPath) == false)
            return metadata;

        // 设置模型绝对路径，用于 Web 端通过 file:// 加载
        metadata.ModelPath = modelJsonPath.Replace('\\', '/');

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            using JsonDocument jsonDoc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
            JsonElement root = jsonDoc.RootElement;

            //收集支持的表情动作
            {
                JsonElement refs = root.TryGetProperty("FileReferences", out JsonElement fileRefs) ? fileRefs : root;
                FileReferencesModel model = JsonSerializer.Deserialize<FileReferencesModel>(refs.GetRawText(), options) ?? new();

                foreach (ExpressionItem exp in model.Expressions)
                {
                    if (string.IsNullOrEmpty(exp.Name) == false)
                        metadata.Expressions.Add(exp.Name);
                }

                foreach ((string groupName, List<MotionItem> items) in model.Motions)
                {
                    for (int index = 0; index < items.Count; index++)
                    {
                        MotionItem item = items[index];
                        string name = string.IsNullOrEmpty(item.Name)
                            ? Path.GetFileNameWithoutExtension(item.File ?? "") //动作 Name 并不是官方规范，所以备用拿 File 名称
                            : item.Name;
                        if (string.IsNullOrEmpty(name) == false)
                            metadata.Motions[name] = (groupName, index);
                    }
                }
            }

            // 交互配置
            if (root.TryGetProperty("Interaction", out JsonElement interactJson))
            {
                foreach (JsonProperty poolProp in interactJson.EnumerateObject())
                {
                    metadata.Interactions[poolProp.Name] =
                        JsonSerializer.Deserialize<List<InteractionItem>>(poolProp.Value.GetRawText(), options) ?? new();
                }
            }
        }
        catch (Exception e)
        {
            AlifeLog.LogError("Live2D模型解析失败\n" + e);
        }

        return metadata;
    }
}