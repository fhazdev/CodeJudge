# CodeJudge

A LeetCode-style platform where users solve coding problems and submit C# that runs against
test cases in an isolated sandbox.

Design: [codejudge-architecture-plan.md](codejudge-architecture-plan.md) for the target
architecture, [docs/build-plan.md](docs/build-plan.md) for decisions, sequencing and the
timeout budget.

**Status: phase 2, local.** Submit code in the browser and get a verdict. The Azure
infrastructure is written but not provisioned.

## Prerequisites

- .NET 10 SDK (10.0.302 or later)
- Node 22 or later
- Docker Desktop

## Running it

Everything below works with no configuration. The Entra app registrations already exist
and their client ids are compiled in as defaults; none of them are secrets.

```powershell
docker compose up -d
dotnet run --project apps/judge/CodeJudge.Judge -- seed
```

Then, in three terminals:

```powershell
dotnet run --project apps/api/CodeJudge.Api          # http://localhost:5199
npm --prefix apps/web run dev                        # http://localhost:5173
dotnet run --project apps/judge/CodeJudge.Judge -- worker
```

The worker is the piece that actually judges. Without it running, submissions sit at
Queued and the UI polls until it gives up.

Sign in with **any Microsoft account**. If a work or school account is blocked by an
administrator, use a personal one; see [the build plan](docs/build-plan.md) for why that
happens and why nothing on our side can fix it.

### Judging from the command line

No API and no sign-in required:

```powershell
dotnet run --project apps/judge/CodeJudge.Judge -- problems

# Judge a file directly, bypassing the queue entirely.
dotnet run --project apps/judge/CodeJudge.Judge -- judge --problem two-sum --file my-solution.cs

# Or exercise the real queue: enqueue, then run one worker execution.
dotnet run --project apps/judge/CodeJudge.Judge -- submit --problem two-sum --file my-solution.cs
dotnet run --project apps/judge/CodeJudge.Judge -- worker --once
dotnet run --project apps/judge/CodeJudge.Judge -- submissions
```

`worker --once` claims at most one message and exits, which is exactly what a Container
Apps Job execution does. The plain `worker` loop exists only for local convenience.

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

The worker tests and the API integration tests do need Docker: they run against a real
Postgres and a real Azurite via Testcontainers, because an in-memory substitute would pass
tests that production fails.

## How judging works

```
POST /api/submissions ──▶ row (Queued) ──▶ Storage Queue ──▶ worker
                                                              │
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
apps/api/CodeJudge.Application       MediatR handlers, DTOs, validators, abstractions
apps/api/CodeJudge.Infrastructure    EF Core, Npgsql, migrations, repositories, seed
apps/api/CodeJudge.Api               controllers, Entra auth, user provisioning
apps/judge/CodeJudge.Judge           compile, orchestrate, verdict
apps/judge/CodeJudge.Judge.Runner    the sandbox child process
apps/web                             React, TypeScript, MSAL, Monaco
infra/bootstrap                      Terraform state prerequisites
infra/terraform/identity             Entra app registrations (applied)
infra/terraform/platform             Azure resources (written, not applied)
docs/                                build plan
```

Remaining: the Container Apps Job for the judge, its Dockerfile, and the deploy workflow.

## Authentication

Multi-tenant plus personal Microsoft accounts, so anyone can sign in. The SPA is a public
client using authorization code flow with PKCE; the API validates tokens against
`/common`, which means there is no single issuer to pin and `Microsoft.Identity.Web`'s
`AadIssuerValidator` does the work.

Two things worth knowing if you touch this:

- The SPA must send the **access token**, not the ID token. The ID token identifies the
  user to the browser app; only the access token carries the API as its audience.
- Redirect URIs must match byte for byte, trailing slash included. `vite.config.ts` pins
  port 5173 with `strictPort` for exactly this reason.
