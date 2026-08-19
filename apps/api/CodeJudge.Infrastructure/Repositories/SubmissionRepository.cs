using CodeJudge.Application.Abstractions;
using CodeJudge.Domain.Entities;
using CodeJudge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodeJudge.Infrastructure.Repositories;

public sealed class SubmissionRepository(CodeJudgeDbContext db) : ISubmissionRepository
{
    public async Task AddAsync(Submission submission, CancellationToken ct = default) =>
        await db.Submissions.AddAsync(submission, ct);

    public Task<Submission?> GetForUserAsync(
        Guid submissionId, Guid userId, CancellationToken ct = default) =>
        db.Submissions
            .AsNoTracking()
            .Include(s => s.Problem)
            // The ownership predicate lives here rather than in the handler so no caller
            // can reach a submission by id alone.
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct);

    public async Task<IReadOnlyList<Submission>> ListForUserAsync(
        Guid userId, Guid? problemId, int skip, int take, CancellationToken ct = default) =>
        await Filter(userId, problemId)
            .AsNoTracking()
            .Include(s => s.Problem)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountForUserAsync(
        Guid userId, Guid? problemId, CancellationToken ct = default) =>
        Filter(userId, problemId).CountAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private IQueryable<Submission> Filter(Guid userId, Guid? problemId)
    {
        var query = db.Submissions.Where(s => s.UserId == userId);

        return problemId is null ? query : query.Where(s => s.ProblemId == problemId);
    }
}
