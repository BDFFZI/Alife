using Alife.Client;

namespace Alife.Client.Android.Backend;

public sealed record NativeHostCommand(string Id, string Action, string? WindowId = null, HtmlWebWindowOptions? Options = null, string? Url = null);
public sealed record NativeHostResult(string Id, string? Error = null);
