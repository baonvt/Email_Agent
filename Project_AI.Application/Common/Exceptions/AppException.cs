using Project_AI.Application.Common.Enums;

namespace Project_AI.Application.Common.Exceptions;

public sealed class AppException : Exception
{
    public AppException(ErrorCode code, string message,
        IReadOnlyDictionary<string, string[]>? errors = null) : base(message)
    {
        Code = code;
        Errors = errors;
    }

    public ErrorCode Code { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}
