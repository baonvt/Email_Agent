namespace Project_AI.Application.Common.Exceptions;

public enum AuthErrorKind
{
    InvalidInput,
    Unauthorized,
    Unavailable
}

public sealed class AuthException(AuthErrorKind kind, string code, string message) : Exception(message)
{
    public AuthErrorKind Kind { get; } = kind;
    public string Code { get; } = code;
}
