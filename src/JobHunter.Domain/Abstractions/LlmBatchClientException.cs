namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The declared provider-agnostic fault of <see cref="ILlmBatchClient"/>: something went wrong talking to the
/// batch provider and the operation could not complete. Each adapter's own fault type derives from this, so a
/// caller that must not fail on a provider outage — the inline narrative synthesiser (F5 T05), which falls
/// back to a template so the digest still ships — can catch a single Domain type rather than reach across the
/// architecture boundary for <c>JobHunter.Claude</c>'s Anthropic-specific exception. It never carries a
/// secret, an API key or any prompt content (invariant 12) — that discipline is the adapter's, and this base
/// type adds nothing to a message.
///
/// <para>Message handlers deliberately let this propagate so Wolverine's retry and dead-letter machinery
/// applies; only the one inline, best-effort caller catches it. A fault escaping any <see cref="ILlmBatchClient"/>
/// method that is a genuine provider fault (as opposed to a caller cancellation) is one of these.</para>
/// </summary>
public class LlmBatchClientException : Exception
{
    public LlmBatchClientException(string message)
        : base(message)
    {
    }

    public LlmBatchClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
