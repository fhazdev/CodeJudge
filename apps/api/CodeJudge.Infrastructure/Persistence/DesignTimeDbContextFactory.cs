using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodeJudge.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. Reads CODEJUDGE_CONNECTION when set so the
/// same commands work against Neon, and otherwise points at the docker compose Postgres.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CodeJudgeDbContext>
{
    public const string LocalConnectionString =
        "Host=localhost;Port=5432;Database=codejudge;Username=codejudge;Password=localdev";

    public CodeJudgeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CODEJUDGE_CONNECTION") ?? LocalConnectionString;

        var options = new DbContextOptionsBuilder<CodeJudgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CodeJudgeDbContext(options);
    }
}
