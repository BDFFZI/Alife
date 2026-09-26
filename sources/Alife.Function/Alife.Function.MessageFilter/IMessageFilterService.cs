using System;
using System.Threading;

namespace Alife.Function.MessageFilter;

public interface IMessageFilterService
{
    void AddMessageReplyRule(MessageReplyRule messageReplyRule, CancellationToken cancellationToken = default);
    void AddMessageReplyGuidance(Func<string> guidance, CancellationToken cancellationToken = default);
}