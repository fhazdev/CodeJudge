using CodeJudge.Domain.Entities;

namespace CodeJudge.Application.Abstractions;

/// <summary>
/// Read access to problems. Returns domain entities; projecting to DTOs is the handler's
/// job, which is what keeps the "never leak HarnessCode" decision in one reviewable place.
/// </summary>
public interface IProblemRepository
{
    Task<IReadOnlyList<Problem>> ListAsync(int skip, int take, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Includes test cases. Null when no problem has that slug.</summary>
    Task<Problem?> GetBySlugAsync(string slug, CancellationToken ct = default);
}

public interface IUserRepository
{
    /// <summary>
    /// Finds or creates the user for a (tenant, object) pair. Identity is the pair, never
    /// the object id alone, which is unique only within a tenant.
    /// </summary>
    Task<User> GetOrCreateAsync(
        string entraTenantId,
        string entraObjectId,
        string? email,
        string? displayName,
        CancellationToken ct = default);
}

/// <summary>The caller's identity, as read from the validated bearer token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The token's <c>tid</c> claim.</summary>
    string TenantId { get; }

    /// <summary>The token's <c>oid</c> claim.</summary>
    string ObjectId { get; }

    string? Email { get; }

    string? DisplayName { get; }
}

/// <summary>Injected rather than called statically so time is controllable in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
