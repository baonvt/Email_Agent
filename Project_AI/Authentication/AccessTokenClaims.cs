using System.Globalization;
using System.Security.Claims;


namespace Project_AI.API.Authentication;

public static class AccessTokenClaims
{
    public static AccessTokenContext? Read(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId)
            || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)
            || !long.TryParse(principal.FindFirstValue("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var exp))
            return null;
        var jti = principal.FindFirstValue("jti");
        var stamp = principal.FindFirstValue("ss");
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > 64 || string.IsNullOrWhiteSpace(stamp) || stamp.Length > 256)
            return null;
        try { return new AccessTokenContext(userId, sessionId, jti, stamp, DateTimeOffset.FromUnixTimeSeconds(exp)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
