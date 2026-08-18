using CodeJudge.Application.Abstractions;
using CodeJudge.Domain.Entities;
using CodeJudge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodeJudge.Infrastructure.Repositories;

public sealed class UserRepository(CodeJudgeDbContext db, IClock clock) : IUserRepository
{
    public async Task<User> GetOrCreateAsync(
        string entraTenantId,
        string entraObjectId,
        string? email,
        string? displayName,
        CancellationToken ct = default)
    {
        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.EntraTenantId == entraTenantId && u.EntraObjectId == entraObjectId, ct);

        if (existing is not null)
        {
            // Display name and email can change on the Entra side. Cheap to keep current.
            if (existing.Email != email || existing.DisplayName != displayName)
            {
                existing.Email = email;
                existing.DisplayName = displayName;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            EntraTenantId = entraTenantId,
            EntraObjectId = entraObjectId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = clock.UtcNow
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException)
        {
            // Two concurrent first requests from the same new user both miss the read
            // above and both insert. The unique index on (tenant, object) makes one of
            // them lose, and the loser should return the winner's row rather than fail
            // a request that did nothing wrong.
            db.Entry(user).State = EntityState.Detached;

            return await db.Users.FirstAsync(
                u => u.EntraTenantId == entraTenantId && u.EntraObjectId == entraObjectId, ct);
        }
    }
}
