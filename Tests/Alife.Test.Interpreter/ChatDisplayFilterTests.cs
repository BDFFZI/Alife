using Alife.Framework;

[TestFixture]
public class ChatDisplayFilterTests
{
    [Test]
    public void ReasoningDisplayPolicy_RespectsClientToggle()
    {
        Assert.That(
            ChatMessageDisplayPolicy.ShouldDisplayAssistantMessage("", "thinking", false, false),
            Is.False);
        Assert.That(
            ChatMessageDisplayPolicy.ShouldDisplayAssistantMessage("", "thinking", false, true),
            Is.True);
    }

    [Test]
    public void InputVisibility_DistinguishesVisibleAndInternalInputs()
    {
        ChatInputSentEventArgs visible = new("user input", ChatInputVisibility.Visible);
        ChatInputSentEventArgs internalInput = new("tool result", ChatInputVisibility.Internal);

        Assert.That(visible.DisplayInputMessageInChat, Is.True);
        Assert.That(internalInput.DisplayInputMessageInChat, Is.False);
    }

    [Test]
    public void InternalInput_CanStillLeadToVisibleAssistantReply()
    {
        ChatInputSentEventArgs internalInput = new("tool result", ChatInputVisibility.Internal);

        Assert.That(internalInput.DisplayInputMessageInChat, Is.False);
        Assert.That(
            ChatMessageDisplayPolicy.ShouldDisplayAssistantMessage("assistant reply", null, false, false),
            Is.True);
    }

    [Test]
    public void DisplayFilter_MapsAllowedExpressionTags()
    {
        Assert.That(
            AssistantDisplayTextFilter.Filter(@"<expression option=""委屈"" />"),
            Is.EqualTo("<委屈>"));
        Assert.That(
            AssistantDisplayTextFilter.Filter(@"<expression option=""思考状"" />"),
            Is.EqualTo("<思考状>"));
    }

    [Test]
    public void DisplayFilter_MapsSpeakToNaturalDialogue()
    {
        Assert.That(
            AssistantDisplayTextFilter.Filter("<speak>测试</speak>"),
            Is.EqualTo("「测试」"));
        Assert.That(
            AssistantDisplayTextFilter.Filter("<speak>   </speak>"),
            Is.EqualTo(""));
    }

    [Test]
    public void DisplayFilter_RemovesOtherXmlFunctionTags()
    {
        Assert.That(
            AssistantDisplayTextFilter.Filter("<bubble>你好</bubble>"),
            Is.EqualTo(""));
        Assert.That(
            AssistantDisplayTextFilter.Filter(@"<expression option=""开心"" />"),
            Is.EqualTo(""));
        Assert.That(
            AssistantDisplayTextFilter.Filter("<unknown>secret</unknown>"),
            Is.EqualTo(""));
        Assert.That(
            AssistantDisplayTextFilter.Filter("hello <partial"),
            Is.EqualTo("hello"));
    }

    [Test]
    public void DisplayFilter_SuppressesBlankAssistantBubbleAfterTagOnlyOutput()
    {
        string display = AssistantDisplayTextFilter.Filter("<bubble>你好</bubble>");

        Assert.That(display, Is.EqualTo(""));
        Assert.That(
            ChatMessageDisplayPolicy.ShouldDisplayAssistantMessage(display, null, false, false),
            Is.False);
    }

    [Test]
    public void DisplayFilter_DoesNotMutateRawAssistantText()
    {
        string raw = "<speak>测试</speak>";
        string display = AssistantDisplayTextFilter.Filter(raw);

        Assert.That(raw, Is.EqualTo("<speak>测试</speak>"));
        Assert.That(display, Is.EqualTo("「测试」"));
    }
}
