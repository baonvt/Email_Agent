using Project_AI.Application.Common.Enums;

namespace Project_AI.Application.Common.Exceptions;

public sealed class AppException(ErrorCode code, string message,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public ErrorCode Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
