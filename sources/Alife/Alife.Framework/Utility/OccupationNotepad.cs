using System;
using System.Collections.Generic;

namespace Alife.Framework;

public class OccupationMarker(OccupationNotepad occupationNotepad, string reason) : IDisposable
{
    public string Reason { get; set; } = reason;

    public void Dispose()
    {
        occupationNotepad.Return(this);
    }
}

public class OccupationNotepad
{
    public OccupationMarker Rent(string reason)
    {
        OccupationMarker occupationMarker = new(this, reason);

        lock (content)
        {
            content.Add(occupationMarker);
        }

        return occupationMarker;
    }
    public void Return(OccupationMarker occupation)
    {
        lock (content)
        {
            content.Remove(occupation);
        }
    }

    public void Query(Action<IReadOnlyList<OccupationMarker>> action)
    {
        lock (content)
        {
            action(content);
        }
    }

    readonly List<OccupationMarker> content = new();
}