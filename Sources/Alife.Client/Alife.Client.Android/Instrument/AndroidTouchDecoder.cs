using System.Globalization;
using System.Text.RegularExpressions;

namespace Alife.Client.Android;

// Protocol-B multi-touch: follow slot zero, publish only complete SYN_REPORT frames.
internal sealed class AndroidTouchDecoder(int minX, int maxX, int minY, int maxY)
{
    int slot;
    int x;
    int y;
    bool active;
    bool hasX;
    bool hasY;
    bool contactStarted;
    public bool IsContactStart { get; private set; }
    public bool Read(string line, out double normalizedX, out double normalizedY)
    {
        normalizedX = normalizedY = 0;
        IsContactStart = false;
        if (line.Contains("SYN_REPORT")) {
            if (!active || !hasX || !hasY) return false;
            IsContactStart = contactStarted;
            contactStarted = false;
            normalizedX = Math.Clamp((x - minX) / (double)(maxX - minX), 0, 1);
            normalizedY = Math.Clamp((y - minY) / (double)(maxY - minY), 0, 1);
            return true;
        }
        var match = Regex.Match(line, @"EV_ABS\s+(ABS_MT_SLOT|ABS_MT_TRACKING_ID|ABS_MT_POSITION_X|ABS_MT_POSITION_Y)\s+([0-9a-fA-F]+)");
        if (!match.Success) return false;
        int value = unchecked((int)uint.Parse(match.Groups[2].Value, NumberStyles.HexNumber));
        string code = match.Groups[1].Value;
        if (code == "ABS_MT_SLOT") slot = value;
        else if (slot == 0) {
            switch (code) {
                case "ABS_MT_TRACKING_ID": active = value >= 0; contactStarted = active; break;
                case "ABS_MT_POSITION_X": x = value; hasX = true; break;
                case "ABS_MT_POSITION_Y": y = value; hasY = true; break;
            }
        }
        return false;
    }
}

