using Alife.Client.Android;
using NUnit.Framework;

namespace Alife.Test.Android;

public class TouchDecoderTests
{
    [Test]
    public void PublishesCompletePrimaryTouchInNormalizedDeviceCoordinates()
    {
        var decoder = new AndroidTouchDecoder(10, 1090, 20, 2420);
        Assert.That(decoder.Read("EV_ABS ABS_MT_TRACKING_ID 00000001", out _, out _), Is.False);
        decoder.Read("EV_ABS ABS_MT_POSITION_X 00000226", out _, out _); // 550
        decoder.Read("EV_ABS ABS_MT_POSITION_Y 000004c4", out _, out _); // 1220
        Assert.That(decoder.Read("EV_SYN SYN_REPORT 00000000", out double x, out double y), Is.True);
        Assert.That(x, Is.EqualTo(.5));
        Assert.That(y, Is.EqualTo(.5));
    }

    [Test]
    public void SecondaryFingerAndReleaseDoNotOverwritePrimaryPosition()
    {
        var decoder = new AndroidTouchDecoder(0, 1000, 0, 1000);
        decoder.Read("EV_ABS ABS_MT_TRACKING_ID 00000001", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_X 00000064", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_Y 000000c8", out _, out _);
        decoder.Read("EV_ABS ABS_MT_SLOT 00000001", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_X 000003e8", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_Y 000003e8", out _, out _);
        decoder.Read("EV_SYN SYN_REPORT 00000000", out double x, out double y);
        Assert.That((x, y), Is.EqualTo((.1, .2)));
        decoder.Read("EV_ABS ABS_MT_SLOT 00000000", out _, out _);
        decoder.Read("EV_ABS ABS_MT_TRACKING_ID ffffffff", out _, out _);
        Assert.That(decoder.Read("EV_SYN SYN_REPORT 00000000", out _, out _), Is.False);
    }

    [Test]
    public void RepeatedTouchRetainsUnchangedAxesBecauseEvdevMayOmitThem()
    {
        var decoder = new AndroidTouchDecoder(0, 100, 0, 100);
        decoder.Read("EV_ABS ABS_MT_TRACKING_ID 00000001", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_X 00000032", out _, out _);
        decoder.Read("EV_ABS ABS_MT_POSITION_Y 00000032", out _, out _);
        decoder.Read("EV_ABS ABS_MT_TRACKING_ID ffffffff", out _, out _);
        decoder.Read("EV_ABS ABS_MT_TRACKING_ID 00000002", out _, out _);
        Assert.That(decoder.Read("EV_SYN SYN_REPORT 00000000", out double x, out double y), Is.True);
        Assert.That((x,y), Is.EqualTo((.5,.5)));
    }
}
