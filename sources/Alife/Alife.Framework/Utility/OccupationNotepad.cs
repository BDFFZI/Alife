using System;
using System.Collections.Generic;

namespace Alife.Framework;

public class OccupationMarker(OccupationNotepad occupationNotepad, int id, string reason) : IDisposable
{
    public int Id { get; init; } = id;
    public string Reason { get; set; } = reason;

    public void Dispose()
    {
        occupationNotepad.Return(this);
    }
}

public class OccupationNotepad
{
    public List<OccupationMarker> Content => content;

    public OccupationMarker Rent(string reason)
    {
        OccupationMarker occupationMarker = new(
            this, content.Count, reason);
        content.Add(occupationMarker);
        return occupationMarker;
    }
    public void Return(OccupationMarker occupation)
    {
        content.Remove(occupation);
    }

    readonly List<OccupationMarker> content = new();
}