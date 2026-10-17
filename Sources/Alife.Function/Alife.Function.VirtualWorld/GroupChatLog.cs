using System;
using System.Collections.Generic;
using Alife.Framework;

namespace Alife.Function.VirtualWorld;

public class WorldGroupMessage
{
    public string Sender { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public bool FromUser { get; set; }
}

public static class GroupChatLog
{
    const int MaxCount = 200;

    static readonly object logLock = new();
    static readonly List<WorldGroupMessage> log = new();
    static StorageSystem? storageSystem;
    static string? messagePrefix;

    const string PrefixStoragePath = "VirtualWorld/GroupMessagePrefix";
    const string DefaultPrefix = "[来自管理员的广播消息]";

    public static void Initialize(StorageSystem system)
    {
        storageSystem ??= system;
    }

    public static string MessagePrefix
    {
        get => messagePrefix ??= storageSystem?.GetObject<string>(PrefixStoragePath) ?? DefaultPrefix;
        set
        {
            messagePrefix = value;
            storageSystem?.SetObject(PrefixStoragePath, value);
        }
    }

    public static void Add(string? sender, string message, bool fromUser)
    {
        lock (logLock)
        {
            log.Add(new WorldGroupMessage {
                Sender = sender ?? "",
                Content = message,
                Time = DateTime.Now,
                FromUser = fromUser
            });
            if (log.Count > MaxCount)
                log.RemoveRange(0, log.Count - MaxCount);
        }
    }

    public static void Clear()
    {
        lock (logLock)
            log.Clear();
    }

    public static List<WorldGroupMessage> GetAll()
    {
        lock (logLock)
            return [.. log];
    }
}
