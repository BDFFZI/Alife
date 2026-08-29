using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Function.GameCompanion.Collector;

/// <summary>
/// 采样器配置列表的 JSON 转换：外部以「{ Name, Sampler, Config }」包装持久化
/// （额外记录每个配置对应的采样器与采样器配置数据），运行时经 CollectorRegistry
/// 还原为具体配置子类实例，不依赖类型元数据序列化。
/// </summary>
public sealed class CollectorConfigListConverter : JsonConverter<List<CollectConfigBase>>
{
    public override void WriteJson(JsonWriter writer, List<CollectConfigBase>? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        if (value != null)
        {
            foreach (CollectConfigBase config in value)
            {
                // 占位配置：直接输出保留的原始数据，不走 FromObject
                if (config is PlaceholderCollectConfig placeholder)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Name");
                    writer.WriteValue(placeholder.Name);
                    writer.WritePropertyName("IsEnable");
                    writer.WriteValue(placeholder.IsEnable);
                    writer.WritePropertyName("IsValidator");
                    writer.WriteValue(placeholder.IsValidator);
                    writer.WritePropertyName("DebounceSeconds");
                    writer.WriteValue(placeholder.DebounceSeconds);
                    writer.WritePropertyName("ExpireSeconds");
                    writer.WriteValue(placeholder.ExpireSeconds);
                    writer.WritePropertyName("ForcePush");
                    writer.WriteValue(placeholder.ForcePush);
                    writer.WritePropertyName("Prerequisite");
                    writer.WriteValue(placeholder.Prerequisite ?? "");
                    writer.WritePropertyName("Sampler");
                    writer.WriteValue(placeholder.SamplerName);
                    writer.WritePropertyName("Config");
                    placeholder.RawConfig.WriteTo(writer);
                    writer.WriteEndObject();
                    continue;
                }

                writer.WriteStartObject();
                writer.WritePropertyName("Name");
                writer.WriteValue(config.Name);
                writer.WritePropertyName("IsEnable");
                writer.WriteValue(config.IsEnable);
                writer.WritePropertyName("IsValidator");
                writer.WriteValue(config.IsValidator);
                writer.WritePropertyName("DebounceSeconds");
                writer.WriteValue(config.DebounceSeconds);
                writer.WritePropertyName("ExpireSeconds");
                writer.WriteValue(config.ExpireSeconds);
                writer.WritePropertyName("ForcePush");
                writer.WriteValue(config.ForcePush);
                writer.WritePropertyName("Prerequisite");
                writer.WriteValue(config.Prerequisite ?? "");
                writer.WritePropertyName("Sampler");
                writer.WriteValue(CollectorRegistry.TypeName(config));
                writer.WritePropertyName("Config");
                JObject data = JObject.FromObject(config, serializer);
                data.Remove("Name");              // 框架通用参数存于外层
                data.Remove("IsEnable");
                data.Remove("IsValidator");
                data.Remove("DebounceSeconds");
                data.Remove("ExpireSeconds");
                data.Remove("ForcePush");
                data.Remove("Prerequisite");
                data.WriteTo(writer);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    public override List<CollectConfigBase>? ReadJson(
        JsonReader reader,
        Type objectType,
        List<CollectConfigBase>? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        var list = new List<CollectConfigBase>();
        foreach (JToken token in array)
        {
            if (token is not JObject entry)
                continue;
            string sampler = entry["Sampler"]?.ToString() ?? "";
            JObject? data = entry["Config"] as JObject;
            CollectConfigBase? config = CollectorRegistry.CreateConfig(sampler, data);
            if (config is null)
                continue;
            config.Name = entry["Name"]?.ToString() ?? "";
            config.IsEnable = entry["IsEnable"]?.Value<bool>() ?? true;
            config.IsValidator = entry["IsValidator"]?.Value<bool>() ?? false;
            // 防抖读外层；旧版本曾存在 Config 内，兼容兜底
            config.DebounceSeconds = entry["DebounceSeconds"]?.Value<double>()
                ?? data?["DebounceSeconds"]?.Value<double>()
                ?? 2.5;
            config.ExpireSeconds = entry["ExpireSeconds"]?.Value<double>()
                ?? data?["ExpireSeconds"]?.Value<double>()
                ?? 1.2;
            config.ForcePush = entry["ForcePush"]?.Value<bool>()
                ?? data?["ForcePush"]?.Value<bool>()
                ?? false;
            string? prereq = entry["Prerequisite"]?.ToString()
                ?? data?["Prerequisite"]?.ToString();
            config.Prerequisite = string.IsNullOrEmpty(prereq) ? null : prereq;
            list.Add(config);
        }
        return list;
    }
}
