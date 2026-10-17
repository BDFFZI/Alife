using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Vision.Qwen;

[Module("Qwen视觉分析",
    "基于Qwen2.5-VL的本地视觉分析引擎",
    defaultCategory: "Alife 官方/模型接入/视觉模型",
    EditorUI = typeof(QwenVisionModelUI))]
public class QwenVisionModel(
    ILogger<QwenVisionModel> logger) :
    ChatBehaviour,
    IVisionModel,
    IConfigurable<QwenVisionModelConfig>
{
    public QwenVisionModelConfig Configuration { get; set; } = null!;

    public async Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pythonPipe.InvokeAsync<string>("query",
                new { image_path = imagePath, question, max_new_tokens = maxResponseTokens });
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    PythonPipeProcess pythonPipe = null!;
    readonly string pythonCode =
        """
        import sys, json, torch
        from PIL import Image
        from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor, BitsAndBytesConfig
        from qwen_vl_utils import process_vision_info

        device = torch.device('cuda')
        model = None
        processor = None

        def init(model_path):
            global model, processor
            quantization_config = BitsAndBytesConfig(
                load_in_4bit=True,
                bnb_4bit_compute_dtype=torch.bfloat16,
                bnb_4bit_use_double_quant=True,
                bnb_4bit_quant_type="nf4"
            )
            model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
                model_path,
                dtype="auto",
                quantization_config=quantization_config,
                device_map="auto",
                attn_implementation="sdpa"
            )
            processor = AutoProcessor.from_pretrained(
                model_path,
                min_pixels=256 * 28 * 28,
                max_pixels=512 * 28 * 28
            )
            return "ready"

        def query(image_path, question, max_new_tokens):
            image = Image.open(image_path).convert("RGB")
            messages = [
                {
                    "role": "user",
                    "content": [
                        {"type": "image", "image": image},
                        {"type": "text", "text": question},
                    ],
                }
            ]
            text = processor.apply_chat_template(
                messages, tokenize=False, add_generation_prompt=True
            )
            image_inputs, video_inputs = process_vision_info(messages)
            inputs = processor(
                text=[text],
                images=image_inputs,
                videos=video_inputs,
                padding=True,
                return_tensors="pt",
            )
            inputs = inputs.to(device)
            with torch.no_grad():
                generated_ids = model.generate(**inputs, max_new_tokens=max_new_tokens)
                generated_ids_trimmed = [
                    out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
                ]
                res = processor.batch_decode(
                    generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
                )
            del inputs, generated_ids
            torch.cuda.empty_cache()
            return res[0].strip()
        """;

    protected override async Task OnAwake()
    {
        const string ModelId = "Qwen/Qwen2.5-VL-3B-Instruct";
        string modelPath = ModelDownloader.EnsureModelExisting(ModelId);
        pythonPipe = new("qwen_vl", pythonCode);
        await pythonPipe.StartAsync();
        await pythonPipe.InvokeAsync<string>("init", modelPath);
    }
    protected override async Task OnDestroy()
    {
        await pythonPipe.DisposeAsync();
    }
}
