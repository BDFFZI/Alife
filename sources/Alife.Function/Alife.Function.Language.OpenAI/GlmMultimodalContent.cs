using System;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

public sealed class GlmVideoUrlContent(Uri url) : KernelContent
{
    public Uri Url { get; } = url;
}

public sealed class GlmFileUrlContent(Uri url) : KernelContent
{
    public Uri Url { get; } = url;
}
