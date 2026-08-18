# CodeJudge

A LeetCode-style platform where users solve coding problems and submit C# that runs against
test cases in an isolated sandbox.

Design: [codejudge-architecture-plan.md](codejudge-architecture-plan.md) for the target
architecture, [docs/build-plan.md](docs/build-plan.md) for decisions, sequencing and the
timeout budget.

**Status: phase 0 complete.** The judge works end to end locally. No Azure, no API, no
frontend yet.

## Prerequisites

- .NET 10 SDK (10.0.302 or later)
- Docker Desktop

## Running it

```powershell
docker compose up -d

dotnet run --project apps/judge/CodeJudge.Judge -- seed
dotnet run --project apps/judge/CodeJudge.Judge -- problems
dotnet run --project apps/judge/CodeJudge.Judge -- judge --problem two-sum --file my-solution.cs
```

A solution is an ordinary `Solution` class, exactly as it would be on LeetCode:

```csharp
public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        var seen = new System.Collections.Generic.Dictionary<int, int>();
        for (var i = 0; i < nums.Length; i++)
        {
            if (seen.TryGetValue(target - nums[i], out var j)) return new[] { j, i };
            seen[nums[i]] = i;
        }
        return new int[0];
    }
}
```

There is no `Main`. Each problem carries a *harness* that supplies one, parses stdin into
typed arguments, calls your method, and writes the result to stdout. The judge compiles the
harness and your submission as two syntax trees in a single Roslyn compilation, so compile
errors still point at your own line numbers.

## Tests

```powershell
dotnet test --solution CodeJudge.slnx
```

The verdict matrix in `apps/judge/tests` needs neither Postgres nor Docker. It does spawn
and kill real child processes for the time-limit and memory-limit cases, so expect a few
seconds.

## How judging works

```
submission ──▶ Roslyn compile (10 s cap)  ──▶ CompileError
                      │
                      ▼
              per test case, in a child process
                 ├─ killed at 2 s          ──▶ TimeLimitExceeded
                 ├─ GC heap limit hit      ──▶ MemoryLimitExceeded
                 ├─ non-zero exit          ──▶ RuntimeError
                 ├─ stdout mismatch        ──▶ WrongAnswer
                 └─ all cases match        ──▶ Accepted
```

The child process is the whole point. Managed code cannot be reliably aborted from inside
its own process, so a `while (true)` is unreachable by any `CancellationToken`. The only
dependable way to enforce a time limit is for a parent to kill a child.

That said, the trust boundary is the child process plus the container, and nothing more.
Submitted code can still read the filesystem and open sockets. See section 5 of the build
plan for what is and is not defended against, and what is deferred.

## Layout

```
apps/api/CodeJudge.Domain            entities and enums, no dependencies
apps/api/CodeJudge.Infrastructure    EF Core, Npgsql, migrations, seed problems
apps/judge/CodeJudge.Judge           compile, orchestrate, verdict
apps/judge/CodeJudge.Judge.Runner    the sandbox child process
docs/                                build plan
```

`apps/api/CodeJudge.Api`, `CodeJudge.Application` and `apps/web` arrive in phase 1;
`infra/terraform` and the queue wiring in phase 2.
