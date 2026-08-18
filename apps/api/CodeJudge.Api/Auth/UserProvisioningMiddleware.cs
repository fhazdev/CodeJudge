using CodeJudge.Application.Abstractions;

namespace CodeJudge.Api.Auth;

/// <summary>
/// Upserts the local users row on the first authenticated request from a given identity.
///
/// There is no registration step in this application. Someone who signs in with an
/// Entra account has, by that act, proven who they are, so the local row exists purely to
/// hang submissions off. Creating it lazily here means no sign-up flow to build and no
/// window where a valid token has no corresponding row.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IUserRepository users)
    {
        if (!currentUser.IsAuthenticated)
        {
            await next(context);
            return;
        }

        try
        {
            await users.GetOrCreateAsync(
                currentUser.TenantId,
                currentUser.ObjectId,
                currentUser.Email,
                currentUser.DisplayName,
                context.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            // A token that authenticated but carries no oid or tid is not something we can
            // recover from, and it means the token is not the shape we expect.
            logger.LogWarning(ex, "Authenticated principal is missing required identity claims");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}

public static class UserProvisioningMiddlewareExtensions
{
    public static IApplicationBuilder UseUserProvisioning(this IApplicationBuilder app) =>
        app.UseMiddleware<UserProvisioningMiddleware>();
}
