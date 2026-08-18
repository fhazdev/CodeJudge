using CodeJudge.Application.Abstractions;
using CodeJudge.Domain.Entities;
using CodeJudge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodeJudge.Infrastructure.Repositories;

public sealed class ProblemRepository(CodeJudgeDbContext db) : IProblemRepository
{
    public async Task<IReadOnlyList<Problem>> ListAsync(
        int skip, int take, CancellationToken ct = default) =>
        await db.Problems
            .AsNoTracking()
            .OrderBy(p => p.Difficulty)
            .ThenBy(p => p.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) =>
        db.Problems.CountAsync(ct);

    public Task<Problem?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Problems
            .AsNoTracking()
            .Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
}
