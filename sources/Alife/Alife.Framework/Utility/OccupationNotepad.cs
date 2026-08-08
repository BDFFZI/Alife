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
    public bool IsOccupied
    {
        get
        {
            lock (content)
            {
                return content.Count != 0;
            }
        }
    }

    public void Query(Action<IReadOnlyList<OccupationMarker>> action)
    {
        lock (content)
        {
            action(content);
        }
    }
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

    readonly List<OccupationMarker> content = new();
}