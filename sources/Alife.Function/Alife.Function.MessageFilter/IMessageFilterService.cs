using System;
using System.Threading;

namespace Alife.Function.MessageFilter;

public interface IMessageFilterService
{
    bool IsEnabledTimestamp { get; }
    void AddMessageReplyRule(MessageReplyRule messageReplyRule, CancellationToken cancellationToken = default);
    void AddMessageReplyGuidance(Func<string> guidance, CancellationToken cancellationToken = default);
}