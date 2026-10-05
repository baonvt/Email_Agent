using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Project_AI.Application.Common.Enums;

namespace Project_AI.API.Responses;

// One place to read HTTP statuses, their public codes, and default error messages.
public static class ResponseCodes
{
    public static HttpStatusCode StatusFor(ErrorCode code) => code switch
    {
        ErrorCode.InvalidInput or ErrorCode.ValidationFailed or ErrorCode.InvalidRegistration
            or ErrorCode.InvalidPassword or ErrorCode.InvalidLink or ErrorCode.InvalidReset => HttpStatusCode.BadRequest,
        ErrorCode.Unauthorized or ErrorCode.InvalidCredentials or ErrorCode.InvalidSession => HttpStatusCode.Unauthorized,
        ErrorCode.Forbidden or ErrorCode.CsrfRejected => HttpStatusCode.Forbidden,
        ErrorCode.NotFound => HttpStatusCode.NotFound,
        ErrorCode.Conflict => HttpStatusCode.Conflict,
        ErrorCode.MethodNotAllowed => HttpStatusCode.MethodNotAllowed,
        ErrorCode.RequestTimeout => HttpStatusCode.RequestTimeout,
        ErrorCode.PayloadTooLarge => HttpStatusCode.RequestEntityTooLarge,
        ErrorCode.UnsupportedMediaType => HttpStatusCode.UnsupportedMediaType,
        ErrorCode.TooManyRequests => HttpStatusCode.TooManyRequests,
        ErrorCode.ServiceUnavailable or ErrorCode.AuthUnavailable => HttpStatusCode.ServiceUnavailable,
        _ => HttpStatusCode.InternalServerError
    };

    public static ErrorCode ErrorFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorCode.InvalidInput,
        HttpStatusCode.Unauthorized => ErrorCode.Unauthorized,
        HttpStatusCode.Forbidden => ErrorCode.Forbidden,
        HttpStatusCode.NotFound => ErrorCode.NotFound,
        HttpStatusCode.MethodNotAllowed => ErrorCode.MethodNotAllowed,
        HttpStatusCode.RequestTimeout => ErrorCode.RequestTimeout,
        HttpStatusCode.Conflict => ErrorCode.Conflict,
        HttpStatusCode.RequestEntityTooLarge => ErrorCode.PayloadTooLarge,
        HttpStatusCode.UnsupportedMediaType => ErrorCode.UnsupportedMediaType,
        HttpStatusCode.TooManyRequests => ErrorCode.TooManyRequests,
        HttpStatusCode.ServiceUnavailable => ErrorCode.ServiceUnavailable,
        _ => (int)status >= 500 ? ErrorCode.InternalError : ErrorCode.InvalidInput
    };

    public static string CodeFor(ErrorCode code) => JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString());

    public static string CodeFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => "success",
        HttpStatusCode.Created => "created",
        HttpStatusCode.Accepted => "accepted",
        HttpStatusCode.NoContent => "no_content",
        _ => CodeFor(ErrorFor(status))
    };

    public static string TitleFor(HttpStatusCode status) => ReasonPhrases.GetReasonPhrase((int)status);

    public static string MessageFor(ErrorCode code) => code switch
    {
        ErrorCode.InvalidInput => "The request contains invalid input.",
        ErrorCode.ValidationFailed => "One or more validation errors occurred.",
        ErrorCode.Unauthorized => "Authentication is required.",
        ErrorCode.Forbidden => "You do not have permission to perform this action.",
        ErrorCode.NotFound => "The requested resource was not found.",
        ErrorCode.Conflict => "The request conflicts with the current resource state.",
        ErrorCode.MethodNotAllowed => "This HTTP method is not allowed for the requested resource.",
        ErrorCode.RequestTimeout => "The request timed out. Please try again.",
        ErrorCode.PayloadTooLarge => "The request body is too large.",
        ErrorCode.UnsupportedMediaType => "The request content type is not supported.",
        ErrorCode.TooManyRequests => "Too many requests. Please try again later.",
        ErrorCode.ServiceUnavailable => "The service is temporarily unavailable. Please try again later.",
        ErrorCode.CsrfRejected => "A trusted Origin and X-InboxAgent-CSRF: 1 header are required.",
        ErrorCode.InvalidRegistration => "The registration details are invalid.",
        ErrorCode.InvalidPassword => "The password does not meet the password requirements.",
        ErrorCode.RegistrationFailed => "Registration could not be completed.",
        ErrorCode.InvalidCredentials => "Unable to sign in. Check your credentials and confirm your email, or try again later.",
        ErrorCode.LoginFailed => "Sign-in could not be completed.",
        ErrorCode.InvalidLink => "The link is invalid or expired.",
        ErrorCode.InvalidReset => "The reset link or new password is invalid.",
        ErrorCode.ResetFailed => "Password reset could not be completed.",
        ErrorCode.InvalidSession => "The session is invalid or expired. Please sign in again.",
        ErrorCode.AuthUnavailable => "Authentication is temporarily unavailable. Please try again later.",
        ErrorCode.LogoutFailed => "Sign-out could not be completed. Please try again.",
        ErrorCode.RoleAssignmentFailed => "The role could not be assigned.",
        _ => "An unexpected error occurred."
    };
}
