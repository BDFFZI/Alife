using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Function.AIModelUtility;

namespace Alife.Function.Memory;

/// <summary>
/// 纯粹且独立的文本向量化器。
/// 内部通过 PythonPipe 加载 safetensors 格式的 BERT 模型，提供嵌入支持。
/// </summary>
public class TextVectorizer : IAsyncDisposable
{
    public static async Task<TextVectorizer> CreateAsync()
    {
        string modelPath = ModelDownloader.EnsureModelExisting("BAAI/bge-small-zh-v1.5");
        var vectorizer = new TextVectorizer(modelPath);
        await vectorizer.InitAsync();
        return vectorizer;
    }

    TextVectorizer(string modelPath)
    {
        this.modelPath = modelPath;
    }

    async Task InitAsync()
    {
        pythonPipe = new("text_embed", PythonCode);
        await pythonPipe.StartAsync();
        await pythonPipe.InvokeAsync<string>("init", modelPath);
    }

    /// <summary>
    /// 将文本转换为向量。
    /// </summary>
    public async Task<float[]> VectorizeAsync(string text)
    {
        if (pythonPipe == null)
            throw new InvalidOperationException("TextVectorizer 未初始化");

        List<float> result = await pythonPipe.InvokeAsync<List<float>>("embed", text);
        return result.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (pythonPipe != null)
        {
            await pythonPipe.DisposeAsync();
            pythonPipe = null;
        }
    }

    readonly string modelPath;
    PythonPipeProcess? pythonPipe;

    const string PythonCode = """
                              import torch
                              from transformers import AutoModel, AutoTokenizer

                              model = None
                              tokenizer = None
                              device = None

                              def init(model_path):
                                  global model, tokenizer, device
                                  device = 'cuda' if torch.cuda.is_available() else 'cpu'
                                  torch_dtype = torch.float16 if device == 'cuda' else torch.float32
                                  tokenizer = AutoTokenizer.from_pretrained(model_path)
                                  model = AutoModel.from_pretrained(model_path, torch_dtype=torch_dtype).to(device)
                                  model.eval()
                                  return f"ready on {device}"

                              def embed(text):
                                  inputs = tokenizer(text, padding=True, truncation=True, max_length=512, return_tensors="pt").to(device)
                                  with torch.no_grad():
                                      output = model(**inputs)
                                  embedding = output.last_hidden_state[:, 0, :].squeeze().float().cpu().tolist()
                                  return embedding
                              """;
}