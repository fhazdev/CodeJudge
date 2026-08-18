using System.Security.Claims;
using CodeJudge.Application.Abstractions;

namespace CodeJudge.Api.Auth;

/// <summary>
/// Reads identity out of the validated token. Nothing here trusts the request body or
/// query string: every value comes from claims that Microsoft.Identity.Web has already
/// verified the signature over.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // v2.0 tokens use the short names; the ClaimTypes.* URIs appear when claim mapping is
    // left on. We turn mapping off in Program.cs, but check both so a configuration change
    // elsewhere cannot silently produce an anonymous user.
    private const string ObjectIdClaim = "oid";
    private const string ObjectIdClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdClaim = "tid";
    private const string TenantIdClaimUri = "http://schemas.microsoft.com/identity/claims/tenantid";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string TenantId => Find(TenantIdClaim, TenantIdClaimUri)
        ?? throw new InvalidOperationException("Token has no tid claim.");

    public string ObjectId => Find(ObjectIdClaim, ObjectIdClaimUri)
        ?? throw new InvalidOperationException("Token has no oid claim.");

    /// <summary>
    /// Personal Microsoft accounts often carry no email claim at all, so this is
    /// legitimately null rather than an error.
    /// </summary>
    public string? Email => Find("preferred_username", ClaimTypes.Upn, ClaimTypes.Email);

    public string? DisplayName => Find("name", ClaimTypes.Name);

    private string? Find(params string[] claimTypes)
    {
        var principal = Principal;
        if (principal is null)
        {
            return null;
        }

        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }
}
