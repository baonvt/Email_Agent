using Project_AI.Application.Common.Enums;

namespace Project_AI.Application.Common.Exceptions;

// Distinguishes a definite rejection from an uncertain outcome of an external write.
public sealed class ReplySendException : Exception
{
    public ErrorCode Code { get; }
    public bool OutcomeUnknown { get; }
    public ReplySendException(ErrorCode code, bool outcomeUnknown) : base("Gmail reply could not be completed.")
    {
        Code = code; OutcomeUnknown = outcomeUnknown;
    }
}
