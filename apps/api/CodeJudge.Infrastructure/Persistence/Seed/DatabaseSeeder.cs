using Microsoft.EntityFrameworkCore;

namespace CodeJudge.Infrastructure.Persistence.Seed;

public static class DatabaseSeeder
{
    /// <summary>
    /// Applies migrations, then upserts the seed problems. Idempotent: seed ids are fixed,
    /// so running this repeatedly converges rather than duplicating.
    /// </summary>
    public static async Task MigrateAndSeedAsync(CodeJudgeDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        foreach (var problem in SeedData.Problems())
        {
            var existing = await db.Problems
                .Include(p => p.TestCases)
                .FirstOrDefaultAsync(p => p.Id == problem.Id, ct);

            if (existing is null)
            {
                db.Problems.Add(problem);
                continue;
            }

            existing.Slug = problem.Slug;
            existing.Title = problem.Title;
            existing.Difficulty = problem.Difficulty;
            existing.StatementMd = problem.StatementMd;
            existing.ConstraintsMd = problem.ConstraintsMd;
            existing.StarterCode = problem.StarterCode;
            existing.HarnessCode = problem.HarnessCode;
            existing.TimeLimitMs = problem.TimeLimitMs;
            existing.MemoryLimitKb = problem.MemoryLimitKb;

            // Replace the case set wholesale. Editing cases in place would leave orphans
            // behind whenever a problem loses a case.
            db.TestCases.RemoveRange(existing.TestCases);
            foreach (var testCase in problem.TestCases)
            {
                db.TestCases.Add(testCase);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
