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
            or ErrorCode.InvalidPassword or ErrorCode.InvalidLink or ErrorCode.InvalidReset
            or ErrorCode.OAuthRejected or ErrorCode.OAuthDenied or ErrorCode.InvalidOAuthState => HttpStatusCode.BadRequest,
        ErrorCode.Unauthorized or ErrorCode.InvalidCredentials or ErrorCode.InvalidSession => HttpStatusCode.Unauthorized,
        ErrorCode.Forbidden or ErrorCode.CsrfRejected => HttpStatusCode.Forbidden,
        ErrorCode.NotFound => HttpStatusCode.NotFound,
        ErrorCode.Conflict or ErrorCode.MailboxReconnectRequired or ErrorCode.MailboxSyncInProgress
            or ErrorCode.AnalysisInProgress or ErrorCode.AnalysisOutdated => HttpStatusCode.Conflict,
        ErrorCode.MethodNotAllowed => HttpStatusCode.MethodNotAllowed,
        ErrorCode.RequestTimeout => HttpStatusCode.RequestTimeout,
        ErrorCode.PayloadTooLarge => HttpStatusCode.RequestEntityTooLarge,
        ErrorCode.UnsupportedMediaType => HttpStatusCode.UnsupportedMediaType,
        ErrorCode.TooManyRequests or ErrorCode.MailboxSyncThrottled or ErrorCode.AiThrottled => HttpStatusCode.TooManyRequests,
        ErrorCode.ServiceUnavailable or ErrorCode.AuthUnavailable or ErrorCode.MailboxNotConfigured
            or ErrorCode.MailboxUnavailable or ErrorCode.AiNotConfigured or ErrorCode.AiUnavailable => HttpStatusCode.ServiceUnavailable,
        ErrorCode.AiInvalidResponse => HttpStatusCode.BadGateway,
        ErrorCode.MailboxHistoryExpired => HttpStatusCode.Conflict,
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

    public static string CodeFor(ErrorCode code) => code switch
    {
        ErrorCode.OAuthRejected => "oauth_rejected",
        ErrorCode.OAuthDenied => "oauth_denied",
        ErrorCode.InvalidOAuthState => "invalid_oauth_state",
        _ => JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString())
    };

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
        ErrorCode.MailboxNotConfigured => "Configure Gmail OAuth before connecting a mailbox.",
        ErrorCode.MailboxUnavailable => "The mailbox provider is temporarily unavailable.",
        ErrorCode.OAuthRejected => "Google authorization was rejected. Start the connection again.",
        ErrorCode.OAuthDenied => "The required mailbox permission was not granted.",
        ErrorCode.InvalidOAuthState => "The authorization request is invalid or expired. Start again in the same browser.",
        ErrorCode.MailboxReconnectRequired => "Reconnect the mailbox to restore Google authorization.",
        ErrorCode.MailboxSyncInProgress => "A mailbox synchronization is already running. Try again later.",
        ErrorCode.MailboxSyncThrottled => "Google temporarily limited synchronization. Try again later.",
        ErrorCode.MailboxHistoryExpired => "The mailbox history expired. Synchronize the inbox again.",
        ErrorCode.AiNotConfigured => "Configure Gemini before analyzing email.",
        ErrorCode.AiUnavailable => "Email analysis is temporarily unavailable. Try again later.",
        ErrorCode.AiThrottled => "The AI provider temporarily limited requests. Try again later.",
        ErrorCode.AiInvalidResponse => "The AI provider did not return a valid email analysis.",
        ErrorCode.AnalysisInProgress => "An analysis of this email is already running.",
        ErrorCode.AnalysisOutdated => "The email or mailbox changed during analysis. Try again.",
        _ => "An unexpected error occurred."
    };
}
