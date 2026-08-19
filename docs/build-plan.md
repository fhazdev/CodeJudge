# CodeJudge, Build Plan

Companion to [codejudge-architecture-plan.md](../codejudge-architecture-plan.md). That document says *what* the system is. This one says *what to build, in what order, and what was decided along the way*.

Status: phases 0 and 1 in progress. Judge core, API and web app all working locally; Azure infrastructure not yet provisioned.

---

## 1. Decisions locked

| # | Decision | Choice | Why |
|---|---|---|---|
| D1 | Container registry | **GHCR** (`ghcr.io/<user>/codejudge-api`, `codejudge-judge`), public images | The architecture doc excluded a registry, but both the API and judge are custom images and must live somewhere. GHCR is free and public images need no pull credentials on Container Apps. Keeps ACR's ~$5/mo off the bill. |
| D2 | Untrusted C# execution | **Roslyn in-memory compile + child process runner** | In-process `CSharpScript` cannot be cancelled: an infinite loop in submitted code is unkillable without tearing down the whole job, so Time Limit Exceeded is undetectable. Compiling with Roslyn skips MSBuild overhead while a child process gives real kill-on-timeout, memory caps, and stdout capture. |
| D3 | API architecture | **Clean Architecture + MediatR (CQRS)** | Consistent portfolio story with the Polaris work. More ceremony than a 7-endpoint API needs, which is acceptable for a piece whose purpose is demonstrating structure. |
| D4 | Terraform state | **Azure Storage backend, bootstrapped by an idempotent script** | Remote state is a hard prerequisite for `cd-infra.yml` running plan/apply in CI. Chicken-and-egg resolved by a small `az`-based bootstrap outside Terraform. ~$0.02/mo. |
| D5 | Problem harness model | **Per-problem harness source compiled alongside the submission** | See section 4. Lets the user write a LeetCode-style `Solution` class while the judge stays a dumb "compile, run, diff stdout" engine with no reflection or type marshalling. |
| D6 | GitHub Actions to Azure auth | **OIDC federated credentials**, no client secrets | Standard practice and a better interview answer than a stored SP secret. |
| D7 | Local development | **docker compose: Postgres + Azurite** | Neither Azure nor Neon should be required to develop. Azurite emulates the Storage Queue. |
| D8 | HTTP layer | **MVC controllers**, not minimal APIs | User preference. Attribute-routed `[ApiController]` classes, thin, dispatching to MediatR. Pairs conventionally with D3: filters, model binding, and `ProblemDetails` handling all sit where a reviewer expects to find them. |
| D9 | Test framework | **xUnit**, all test projects | User preference. Also the default for new .NET template projects and the framework most .NET reviewers read fastest. See section 8. |

### Amendments to the architecture doc

- **§4** should add the GHCR decision. "Reference public images like `python:3.12-slim` directly" works for a hypothetical polyglot judge, not for the two custom images this design actually needs.
- **§4** should add the Terraform state backend as a resource, plus the GitHub OIDC app registration (a third `azuread_application`, not two).
- **§6 step 6** changes per D2.
- **§5** data model needs the harness fields and a corrected user uniqueness constraint (see section 3).
- **§9 phase 2** should note that KEDA only *triggers* the job on queue depth. It does not hand the message to the container. The job must dequeue and delete the message itself, or KEDA re-triggers forever.

---

## 2. Repo layout

```
CodeJudge.sln
docker-compose.yml                    Postgres + Azurite for local dev
/apps
  /api
    CodeJudge.Domain/                 entities, enums, value objects, no dependencies
    CodeJudge.Application/            MediatR handlers, DTOs, validators, interfaces
    CodeJudge.Infrastructure/         EF Core DbContext, Npgsql, queue client, migrations
    CodeJudge.Api/                    MVC controllers, auth wiring, Dockerfile
      Controllers/                    ProblemsController, SubmissionsController, HealthController
    tests/
      CodeJudge.Application.Tests/    xUnit, unit
      CodeJudge.Api.IntegrationTests/ xUnit, Testcontainers-backed
  /judge
    CodeJudge.Judge/                  worker: dequeue, orchestrate, write verdict, Dockerfile
    CodeJudge.Judge.Runner/           child process host that runs the compiled assembly
    tests/CodeJudge.Judge.Tests/      xUnit, unit + verdict matrix
  /web
    src/                              React + TS + Vite + Monaco + MSAL
/infra
  /bootstrap/bootstrap.ps1            creates state RG + storage account + GitHub OIDC app
  /terraform                          main.tf, providers.tf, variables.tf, per-resource files
/db
  /seed                               problem + test case seed data (JSON)
/docs
  build-plan.md                       this file
/.github/workflows
```

`CodeJudge.Judge` references `CodeJudge.Domain` and `CodeJudge.Infrastructure` so the schema is defined once. It does not reference `CodeJudge.Application`.

---

## 3. Data model (corrected)

```sql
create table users (
  id               uuid primary key,
  entra_object_id  text not null,
  entra_tenant_id  text not null,
  email            text,
  display_name     text,
  created_at       timestamptz not null default now(),
  unique (entra_tenant_id, entra_object_id)
);

create table problems (
  id             uuid primary key,
  slug           text not null unique,
  title          text not null,
  difficulty     text not null,           -- Easy | Medium | Hard
  statement_md   text not null,
  constraints_md text,
  starter_code   text not null,           -- what loads into Monaco
  harness_code   text not null,           -- see section 4
  time_limit_ms  int  not null default 2000,
  memory_limit_kb int not null default 262144,
  created_at     timestamptz not null default now()
);

create table test_cases (
  id              uuid primary key,
  problem_id      uuid not null references problems(id) on delete cascade,
  ordinal         int  not null,
  input           text not null,          -- fed to the harness on stdin
  expected_output text not null,
  is_hidden       boolean not null default true
);

create table submissions (
  id            uuid primary key,
  user_id       uuid not null references users(id),
  problem_id    uuid not null references problems(id),
  language      text not null default 'csharp',
  code          text not null,
  status        text not null,            -- see enum below
  runtime_ms    int,
  memory_kb     int,
  failed_case_ordinal int,
  stderr_excerpt      text,               -- compile errors / exception text, truncated
  attempt_count int not null default 0,
  created_at    timestamptz not null default now(),
  completed_at  timestamptz
);
create index on submissions (user_id, problem_id, created_at desc);
```

**Correction to §5**: `entra_object_id` (`oid`) is only unique *within a tenant*. With multi-tenant plus personal accounts, identity is the `(tid, oid)` pair. A unique constraint on `oid` alone would eventually collide.

Status enum: `Queued`, `Running`, `Accepted`, `WrongAnswer`, `TimeLimitExceeded`, `RuntimeError`, `CompileError`, `MemoryLimitExceeded`, `InternalError`.

`CompileError` and `InternalError` are additions to §5. Compile failure is the single most common outcome when practicing and deserves its own verdict rather than being folded into `RuntimeError`. `InternalError` is the poison-message terminal state.

---

## 4. The harness model (D5)

The problem, not the judge, knows how to call the user's code.

Each problem stores `harness_code`: a C# snippet containing `Main`, which parses stdin into typed arguments, calls the user's `Solution` class, and writes the result to stdout. The judge compiles `harness_code` and the submitted code as two syntax trees in one compilation, runs the resulting assembly, and diffs stdout against `expected_output`.

For Two Sum, `harness_code` would be roughly:

```csharp
using System;
using System.Linq;
using System.Text.Json;

static class Harness {
    static void Main() {
        var nums   = JsonSerializer.Deserialize<int[]>(Console.ReadLine()!)!;
        var target = int.Parse(Console.ReadLine()!);
        var result = new Solution().TwoSum(nums, target);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
```

Why this over the alternatives:

- **vs. reflection-based invocation**: no runtime type marshalling, no signature-matching code, no ambiguity when a user changes a parameter type. If the signature is wrong, they get a compile error naming the exact problem, which is the correct feedback anyway.
- **vs. raw stdin/stdout (Codeforces style)**: the user still writes a clean `Solution` class, which is what the project is imitating.
- **Adding a language later** means writing one new harness per problem, not rearchitecting the judge.

Test cases stay plain text. Output comparison trims trailing whitespace per line and trailing newlines at end of output, then compares exactly.

---

## 5. Judge execution design (D2)

Two processes inside one job execution.

**`CodeJudge.Judge` (parent)**
1. Dequeue one message from `submissions` queue, visibility timeout 10 min (see the timeout budget below).
2. If `dequeue_count > 3`, set status `InternalError`, delete message, exit.
3. Load submission, problem, and test cases. Set status `Running`.
4. Compile `harness_code` + submitted code with `CSharpCompilation` to an in-memory `MemoryStream`, referencing a curated allowlist of assemblies, under a 10 s compile cap. On failure: status `CompileError`, diagnostics into `stderr_excerpt`, done.
5. Write the assembly to a temp path. For each test case, spawn `CodeJudge.Judge.Runner` as a child process:
   - stdin: the test case input
   - kill the process at `time_limit_ms`, verdict `TimeLimitExceeded`
   - non-zero exit: `RuntimeError`, stderr captured
   - stdout mismatch: `WrongAnswer` with `failed_case_ordinal`
   - short-circuit on the first failure
6. Write verdict, `runtime_ms` (max across cases), `completed_at`.
7. **Delete the queue message.** Non-negotiable, or KEDA re-triggers indefinitely.

**`CodeJudge.Judge.Runner` (child)** is a thin host: `AssemblyLoadContext.LoadFromAssemblyPath`, invoke the entry point, flush stdout, exit. It exists purely to be killable.

### Timeout budget

Five nested timeouts. Each layer must be strictly and visibly larger than the one inside it, or the wrong layer fires and the verdict is wrong.

| Layer | Value | Enforced by | Guards against | On expiry |
|---|---|---|---|---|
| Queue visibility | **600 s** | Storage Queue | job dies without deleting the message | message reappears, `dequeue_count` increments |
| Job `replicaTimeout` | **300 s** | Container Apps | the parent itself hangs | execution killed; message reappears later |
| Whole submission | **90 s** | parent, wall clock | many slow-but-passing cases summing up | `TimeLimitExceeded`, verdict written |
| Compile | **10 s** | parent, cancellation token | pathological input to Roslyn | `CompileError`, "compilation timed out" |
| Per test case | **2 000 ms** | parent kills the child | the ordinary TLE case | `TimeLimitExceeded` with `failed_case_ordinal` |

Per test case is `problems.time_limit_ms`, overridable per problem for a legitimately heavier one. The other four are fixed configuration.

Why each gap is sized the way it is:

- **600 s visibility vs. 300 s `replicaTimeout`.** These must not be equal. If they match, a message can become visible again at the same moment the job is finishing, and a second execution judges the same submission concurrently. Intermittent and unpleasant to diagnose. Double the ceiling is a clean margin.
- **300 s `replicaTimeout` vs. 90 s submission budget.** `replicaTimeout` should never be the thing that fires in normal operation, because when Container Apps kills the execution, no verdict is written and the submission is stuck in `Running` until the message redelivers. The parent's own 90 s cap is what should fire, because the parent is still alive to write a real verdict. `replicaTimeout` is purely the backstop for a hung or crashed parent.
- **90 s submission vs. 2 s per case.** Worst realistic case: 20 test cases at 2 s each is 40 s, plus up to 10 s compile, plus roughly 100 ms of process-spawn overhead per case, plus database round trips. That lands near 55 s. 90 s leaves headroom without letting a pathological submission camp on the free tier grant. **Cap problems at 20 test cases** so this arithmetic holds.

**Compile timeout is not optional.** Roslyn is not linear in input size, and known pathological inputs (deeply nested generics, enormous constant expressions) can make the compiler work for a very long time on a physically small file. The input here is untrusted by definition, so an uncapped compile is a denial-of-service against your own free tier.

One deliberate inconsistency: the SPA gives up polling at 120 s (section 7) while the judge may take up to 300 s in the `replicaTimeout` backstop case. The SPA giving up is a display concession, not a cancellation. Judging continues, the verdict lands in the database, and a page refresh shows it. The UI should say "still running, refresh to check" rather than reporting a failure.

### Isolation posture, stated honestly

The trust boundary is the container plus the child process, and that is it. Submitted code can read the filesystem, open sockets, and start processes. Mitigations actually in place:

- one container execution per submission, torn down afterward
- a five-layer timeout budget capping compile, each test case, and the submission as a whole, with `replicaTimeout` as the outer backstop (see above)
- CPU/memory caps on the job (0.5 vCPU / 1 GiB)
- no secrets in the judge container's environment beyond what it needs; the DB connection is pulled from Key Vault at startup and never written to disk
- Entra sign-in required to submit at all, so submissions are attributable

The honest interview answer, and the phase 4 item: real isolation means a network-restricted VNet-integrated environment, a non-root user, a read-only root filesystem, and a seccomp profile. Worth naming as known-and-deferred rather than pretending the container boundary is sufficient.

**Queue message contract** (code stays in the DB, queue messages are capped at 64 KB):

```json
{ "submissionId": "3f2a...", "enqueuedAt": "2026-08-18T12:00:00Z" }
```

---

## 6. Auth specifics

Three app registrations, not two:

1. **API** (`codejudge-api`): exposes scope `access_as_user`. `signInAudience = AzureADandPersonalMicrosoftAccount`.
2. **SPA** (`codejudge-web`): public client, SPA redirect URI, PKCE, pre-authorized against the API's scope so no consent prompt appears for the API scope itself.
3. **GitHub Actions OIDC** (`codejudge-cicd`): federated credentials scoped to `repo:<user>/CodeJudge:ref:refs/heads/main` and `:pull_request`, with Contributor on the subscription.

### Sign-in flow

The SPA is a **public client** using OAuth 2.0 authorization code flow with PKCE. Public because a browser app cannot keep a secret; anything shipped is readable in devtools. PKCE replaces the client secret with a per-request proof.

```
1. User loads the SWA and clicks "Sign in".

2. MSAL.js generates a random code_verifier, hashes it into a
   code_challenge, and redirects to:
     https://login.microsoftonline.com/common/oauth2/v2.0/authorize
       ?client_id=<spa-client-id>
       &response_type=code
       &redirect_uri=https://<swa-host>
       &scope=openid profile api://<api-client-id>/access_as_user
       &code_challenge=<hash>&code_challenge_method=S256

3. Microsoft hosts the login page. Because signInAudience is
   AzureADandPersonalMicrosoftAccount, the user may pick a work/school
   account from ANY tenant, or a personal Microsoft account.
   The app never sees a password, and stores no password.

4. First time only: consent. "CodeJudge wants to sign you in and read
   your profile."

5. Microsoft redirects back with ?code=<auth-code>.

6. MSAL POSTs the code plus the original code_verifier to /token.
   Microsoft verifies the verifier hashes to the challenge from step 2,
   proving the client that started the flow is the one finishing it.
   Returns an ID token, an access token, and a refresh token.

7. Tokens cached; SPA renders as signed in. Before each API call,
   acquireTokenSilent returns the cached access token or refreshes it.
```

**The two-token distinction**, which is the most commonly botched part:

- The **ID token** says *who the user is*. It is for the SPA, to render "Signed in as …". It never goes to the API.
- The **access token** says *this client may call that API on this user's behalf*. Its `aud` is the API's client ID and it carries the `access_as_user` scope. This is what goes in `Authorization: Bearer`.

Sending the ID token to the API produces a 401 with an audience-mismatch message that reads as baffling until you know the distinction.

### Token validation

Multi-tenant validation is the detail that trips people up: with `/common` as the authority you cannot validate a single fixed issuer, because every tenant produces a different one. Use **`Microsoft.Identity.Web`** rather than hand-rolling `JwtBearer`; its `AadIssuerValidator` accepts any `https://login.microsoftonline.com/{tid}/v2.0` issuer and validates the tenant segment against the token's own `tid` claim. Hand-rolling this leads directly to the temptation of `ValidateIssuer = false`, which is a genuine security hole.

A `CurrentUserMiddleware` upserts the `users` row on first authenticated request, keyed on `(tid, oid)`.

### Configuration that must be exact

| Setting | Value | Consequence if wrong |
|---|---|---|
| SPA platform type | **Single-page application**, not "Web" | Registering as "Web" makes the token endpoint reject the browser origin with a CORS error. Very common mistake |
| Redirect URIs | the SWA URL **and** `http://localhost:5173` | Without localhost you cannot develop |
| `signInAudience` | `AzureADandPersonalMicrosoftAccount` | The default is single-tenant, so nobody outside your tenant can sign in at all |
| Authority | `https://login.microsoftonline.com/common` | `/organizations` silently excludes personal accounts |
| API pre-authorization | SPA client ID listed on the API app | Otherwise a second consent prompt for the API scope |

### Which Microsoft account to use

Three kinds of account can reach the sign-in page, and they behave differently. This matters because the moment it matters most is an interviewer trying the live demo.

| Account type | Example | `tid` claim | Works? |
|---|---|---|---|
| Personal Microsoft account (MSA) | `you@outlook.com`, `you@hotmail.com`, or any address registered as an MSA, including Gmail ones | always `9188040d-6c67-4c5b-b112-36a304b66dad` | **Yes, always.** No tenant admin exists to block it |
| Work/school account, consent allowed | `someone@contoso.com` | that tenant's GUID | Yes. One extra consent screen on first sign-in |
| Work/school account, consent restricted | `someone@bigcorp.com` | that tenant's GUID | **No.** "Need admin approval." Dead end |

**Recommended for demos: a personal Microsoft account.** It is the only category that cannot be blocked by someone else's policy.

The third row is the realistic failure mode and there is nothing you can do about it from your side. `access_as_user` is a user-consentable delegated scope, so *by default* any user can approve it themselves without an admin. But many enterprises turn user consent off tenant-wide, and in those tenants a first-time sign-in to an unknown multi-tenant app stops at an admin approval screen. Your app never gets a token, and no code change fixes it.

So the README and the demo page get an explicit line:

> Sign in with a personal Microsoft account (outlook.com, hotmail.com, or any address you have registered as a Microsoft account). Work or school accounts also work, but some companies block sign-in to third-party apps, in which case you will see "Need admin approval."

Two related notes:

- The account you develop with, `fjhshadow@gmail.com`, is itself a personal MSA that owns the Azure subscription. It signs in fine, but it is a poor test of the flow, because it lives in the same directory that owns the app registration. **Test with a second, unrelated Microsoft account during phase 1**, before the auth wiring calcifies. A first-time external sign-in is the path that actually exercises consent, and it is the one that breaks.
- Sign-in is required to submit at all, which is what makes submissions attributable. That is load-bearing for the isolation posture in section 5, not just a login feature.

---

## 7. API surface

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/api/problems` | required | list, paged |
| GET | `/api/problems/{slug}` | required | statement + starter code, never `harness_code` or hidden test cases |
| POST | `/api/submissions` | required | body: `{ problemSlug, language, code }` → 202 + submission id |
| GET | `/api/submissions/{id}` | required, owner only | polled by the SPA |
| GET | `/api/submissions?problemSlug=` | required | the user's history for a problem |
| GET | `/health` | anonymous | liveness, used by Container Apps probe |

Implemented as attribute-routed controllers (D8). Controllers stay thin: bind, dispatch to MediatR, map the result to a status code. No business logic, no `DbContext`, no `if` beyond mapping a handler result to `200`/`202`/`404`.

```csharp
[ApiController]
[Route("api/problems")]
[Authorize]
public sealed class ProblemsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProblemSummaryDto>>> List(
        [FromQuery] ListProblemsQuery query, CancellationToken ct)
        => Ok(await sender.Send(query, ct));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ProblemDetailDto>> Get(string slug, CancellationToken ct)
        => await sender.Send(new GetProblemBySlugQuery(slug), ct) is { } problem
            ? Ok(problem)
            : NotFound();
}
```

Supporting wiring that comes with the controller choice:

- `[Authorize]` at the controller level, `[AllowAnonymous]` on `HealthController`, rather than per-endpoint `RequireAuthorization()` calls
- a `ValidationExceptionFilter` translating FluentValidation failures into RFC 7807 `ProblemDetails`, registered globally in `AddControllers`
- `[ProducesResponseType]` attributes driving accurate Swagger output
- `CreatedAtAction(nameof(SubmissionsController.Get), ...)` for the 202 on submit, giving the SPA its poll URL in the `Location` header

Polling: 1 s interval, backing off to 3 s after 10 s, giving up at 120 s. Cold start on a scaled-to-zero job is the dominant latency, roughly 10 to 30 seconds for the first submission after idle. Design the UI to say so rather than looking broken.

Giving up is a display concession, not a cancellation: the judge keeps running and the verdict still lands in the database, so the message is "still running, refresh to check", never an error. See the timeout budget in section 5.

---

## 8. Testing (D9)

**xUnit across all three test projects.** Package set, pinned centrally in `Directory.Packages.props` so the projects stay consistent:

| Package | Role | Note |
|---|---|---|
| `xunit.v3` | test framework | xUnit v3 is the current line. Ships its own in-process runner, so test projects are executable |
| `xunit.runner.visualstudio` | VS / `dotnet test` adapter | |
| `Microsoft.NET.Test.Sdk` | test host | |
| `NSubstitute` | mocking | Substituting `IQueueClient`, `IClock`, `ISubmissionRepository` in handler tests |
| `Shouldly` | assertions | See the licensing note below |
| `Testcontainers.PostgreSql` | integration | Real Postgres per integration test class, no in-memory provider |
| `Microsoft.AspNetCore.Mvc.Testing` | integration | `WebApplicationFactory` for controller tests |

Two package choices worth being deliberate about, since both have a trap:

- **Assertions.** FluentAssertions is the reflex choice in .NET, but v8 moved to a paid commercial license. A personal portfolio project qualifies for free use, so it is legal here, but putting it in a public repo that a prospective employer might copy from is a needless hazard. Shouldly is MIT and unencumbered. `AwesomeAssertions`, the community fork of FluentAssertions v7, is the other free option if you specifically want the `.Should().Be()` syntax.
- **Mocking.** NSubstitute over Moq. Moq's 4.20 release briefly shipped SponsorLink, which harvested developer email hashes at build time; it was pulled, but the trust cost lingers and reviewers notice.

### What actually gets tested

The point is coverage of the risky parts, not a coverage percentage.

**`CodeJudge.Judge.Tests`, the one that matters most.** The verdict matrix is the highest-value test suite in the project, since the judge is the component that can be subtly and silently wrong. One `[Theory]` driving a known submission to a known verdict:

```csharp
public sealed class VerdictTests(JudgeFixture fixture) : IClassFixture<JudgeFixture>
{
    [Theory]
    [InlineData("correct.cs",        SubmissionStatus.Accepted)]
    [InlineData("off-by-one.cs",     SubmissionStatus.WrongAnswer)]
    [InlineData("infinite-loop.cs",  SubmissionStatus.TimeLimitExceeded)]
    [InlineData("missing-brace.cs",  SubmissionStatus.CompileError)]
    [InlineData("wrong-signature.cs",SubmissionStatus.CompileError)]
    [InlineData("null-deref.cs",     SubmissionStatus.RuntimeError)]
    public async Task ProducesExpectedVerdict(string fixtureFile, SubmissionStatus expected)
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(), Fixtures.Read(fixtureFile), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(expected);
    }
}
```

(`alloc-bomb.cs` sits in its own `[Fact]` rather than the theory, because the memory path is worth being able to run and read in isolation.)

Submissions live as `.cs` files under `Fixtures/`, not as string literals, so they stay readable and the editor still highlights them. Note that `infinite-loop.cs` and `alloc-bomb.cs` genuinely spawn a child process and kill it. These are slow (a second or two each) and that is fine; they are the tests proving D2 was the right call, and the ones that would have silently failed under in-process scripting.

Also covered: output comparison (trailing whitespace, trailing newline, CRLF vs LF), and Roslyn diagnostic line numbers mapping to the submission's own lines rather than the harness's. That last one is a risk-table item, so it gets an explicit test.

**`CodeJudge.Application.Tests`.** Handler-level, with substituted repositories and queue client. Submission creation enqueues exactly once; a user cannot read another user's submission; `harness_code` and hidden test cases never appear in a problem DTO. That last one is a data-leak test, worth writing even though it looks trivial, because a careless AutoMapper profile is exactly how hidden test cases end up in an API response.

**`CodeJudge.Api.IntegrationTests`.** `WebApplicationFactory` plus a Testcontainers Postgres, with auth stubbed by a test authentication handler that injects a fixed `(tid, oid)`. Real EF Core against real Postgres, since the in-memory provider does not model Postgres behavior and will pass tests that production fails. Covers routing, status codes, the `Location` header on the 202, and the `ProblemDetails` shape on validation failure.

xUnit's per-class parallelism plus one container per collection keeps this manageable; share the container via an `ICollectionFixture` rather than per class, or the suite spends most of its time starting Postgres.

### CI

`ci-api.yml` and `ci-judge.yml` both run `dotnet test --solution CodeJudge.slnx --coverage --report-trx`.

Note the MTP flags rather than the VSTest ones: `--collect:"XPlat Code Coverage"` and `--logger trx` are VSTest options and are silently ignored (or rejected) here. Judge tests need Docker on the runner for Testcontainers from phase 1 onward, which `ubuntu-latest` provides by default; the phase 0 verdict matrix needs no container at all.

---

## 9. Build phases

### Phase 0, foundations (no Azure yet) — **complete**
- [x] `.gitignore`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `CodeJudge.slnx`
- [x] `docker-compose.yml`: Postgres 16 + Azurite
- [x] Domain entities + EF Core DbContext + `InitialSchema` migration
- [x] Seed data: 3 problems (Two Sum, Valid Parentheses, Reverse Linked List) with harness + test cases
- [x] Judge core as a console app running locally against docker compose, no Azure, no queue
- [x] xUnit verdict matrix covering every status, including TLE, CompileError, and MemoryLimitExceeded

**Exit criterion met.** Against the seeded database:

```
judge --problem two-sum --file good.cs   →  Accepted           72 ms
judge --problem two-sum --file loop.cs   →  TimeLimitExceeded  2061 ms, case #1
```

34 tests green in ~6 s. The 2,061 ms is the 2,000 ms per-case limit plus kill overhead, which is the number that could not exist under in-process scripting.

#### What phase 0 changed about the plan

| Planned | Built | Why |
|---|---|---|
| Seed data as JSON under `/db/seed` | `SeedData.cs` with raw string literals | Every meaningful field is multi-line C# or Markdown. Raw string literals carry that with zero escaping; JSON cannot. Also compiled and type-checked |
| `.sln` | `.slnx` | .NET 10's `dotnet new sln` emits the XML format now |
| Roslyn at latest (5.9.0) | Roslyn 5.0.0 | EF Core's design-time package has an exact-version dependency on Roslyn 5.0.0 via CodeAnalysis.CSharp.Workspaces. With transitive pinning on, taking 5.9.0 breaks restore with NU1107 |
| EF Core version implicit | EF Core trio pinned at 10.0.11 | Npgsql 10.0.3 wants EF Core ≥ 10.0.4 while Design carries 10.0.11. Unpinned, the judge binds 10.0.4 while Infrastructure compiles against 10.0.11, failing at CS1705 |
| `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` + `coverlet.collector` | `xunit.v3` + `Microsoft.Testing.Extensions.CodeCoverage` only | xUnit v3 hosts Microsoft.Testing.Platform. The .NET 10 SDK dropped the VSTest bridge, so any of those three re-engages VSTest and `dotnet test` fails outright. MTP mode is opted into in `global.json` |
| `CodeJudge.Judge.Runner` located implicitly | `apps/judge/Runner.targets`, shared | The runner must sit beside whatever spawns it, which is both the judge and the test project. `ReferenceOutputAssembly=false` builds and copies it without linking against it, keeping the process boundary honest |

Also added beyond the checklist: an environment scrub on the child process (the parent holds a database connection string, the child hosts untrusted code, and those two should not meet), and a reference allowlist proving a submission cannot reach EF Core or Npgsql even though the judge process has both loaded.

### Phase 1, skeleton + auth — **auth complete, infrastructure outstanding**
- [x] Entra app registrations, as Terraform in `infra/terraform/identity` (local state)
- [x] API: MediatR + Clean Architecture skeleton, `Microsoft.Identity.Web`, problems endpoints
- [x] Web: Vite + React + TS, MSAL sign-in, problem list, Monaco on the detail page
- [x] **Sign-in verified end to end against real Entra.** The risk-table item is closed
- [x] API Dockerfile, verified: 106 MB, non-root uid 1654, health 200, 401 unauthenticated
- [x] `infra/bootstrap/bootstrap.ps1`: state RG, storage account, container, CI role assignment
- [x] Terraform platform module: RG, Log Analytics, Container Apps environment, storage + queue, Key Vault, UAMI, Container App, Static Web App. Written and validated, **not applied**
- [x] `ci-dotnet.yml`, `ci-web.yml`, `cd-infra.yml`, every command verified locally
- [ ] Verify with a **second, unrelated** Microsoft account, not the subscription owner
- [ ] Neon project created, connection string into Key Vault
- [ ] Run bootstrap, uncomment the backend, `terraform apply`

**Exit criterion:** signed in with a personal Microsoft account on the deployed SWA, seeing the problem list from the live API. Met locally; the deployed half waits on the platform module.

#### What phase 1 changed about the plan

| Planned | Built | Why |
|---|---|---|
| Two app registrations | Three, one gated behind a variable | GitHub Actions OIDC needs its own. It is not created until `var.github_repository` is set, because a federated credential aimed at a repository that does not exist looks configured while granting nothing |
| One Terraform root module | Split `identity` and `platform` | Entra registrations are tenant-scoped, near-static, and must survive a `terraform destroy` of the infrastructure. Splitting also let identity be applied before any state backend existed |
| Enum serialization unconsidered | `JsonStringEnumConverter` | Writing the TypeScript types surfaced that the API was sending `"difficulty": 0`. Every client would have hardcoded ordinals, and inserting an enum value later would silently reinterpret stored responses |

Three Entra constraints that cost a plan/apply cycle each, now encoded in the Terraform: root redirect URIs require a trailing slash; **every** registration allowing personal accounts must request v2 tokens, including the SPA that exposes no scopes; and the identifier URI needs a separate resource because it embeds the client id that does not exist until after creation.

Later in the phase, four more:

| Planned | Built | Why |
|---|---|---|
| `ci-api.yml` and `ci-judge.yml` | One `ci-dotnet.yml` | They are one solution. Splitting them means restoring and building the same shared projects twice per push to prove the same thing |
| Bootstrap creates the CI/CD app registration | Bootstrap creates only its **role assignment** | The registration is ordinary Terraform (identity module). The Contributor grant cannot be, because CI needs that grant in order to run `terraform apply` at all |
| `--collect:"XPlat Code Coverage" --logger trx` | `--coverage --report-trx` | VSTest flags are silently ignored under MTP. `--report-trx` additionally needs `Microsoft.Testing.Extensions.TrxReport` referenced, or the run fails with "Zero tests ran" and no useful message |
| Container app image set by Terraform | `ignore_changes` on the image | CI owns the tag after the first deploy. Without this, every `terraform apply` rolls the app back to whatever the variable holds, silently undoing the newest deployment |

The container app also ships with a deliberately wrong default image (`mcr.microsoft.com/dotnet/samples:aspnetapp`). Container Apps requires a resolvable image at creation time, and the GHCR image does not exist until CI has pushed one, so the first apply would otherwise fail on a chicken-and-egg.

### Phase 2, judge pipeline — **local half complete**
- [x] Storage Queue publish on `POST /api/submissions`
- [x] Judge worker: `worker` and `worker --once`, poison handling, verdict write-back
- [x] SPA submission + polling + results panel
- [x] UAMI role assignments (done early, in the phase 1 platform module)
- [ ] `azurerm_container_app_job` with `azure-queue` scale rule and managed-identity auth
- [ ] Judge Dockerfile
- [ ] `ci-judge.yml` + `cd-deploy.yml` (GHCR push, then `az containerapp update` / `job update`)

**Local exit criterion met.** Four submissions through the real Azurite queue, one worker
execution each:

```
good.cs    -> Accepted           66 ms
wrong.cs   -> WrongAnswer        72 ms   case #1
loop.cs    -> TimeLimitExceeded  2061 ms case #1
broken.cs  -> CompileError
(5th run)  -> NoWork
```

**Deployed exit criterion outstanding**, pending the Container Apps Job.

#### What phase 2 changed about the plan

| Planned | Built | Why |
|---|---|---|
| Judge worker as a dequeue loop | `ProcessNextAsync`, one message per call, with `--once` and a loop wrapper | A Container Apps Job execution *is* one unit of work: KEDA starts a container and it exits. Shaping the core around a loop would mean the local and deployed shapes differ in the one component hardest to debug remotely |
| Submissions enqueued, then judged | Row written first, enqueue second, `InternalError` if the enqueue throws | Enqueue-first risks a message arriving before the row exists. Write-first risks a stranded `Queued` row, so that case is marked terminal immediately rather than leaving a spinner forever. A transactional outbox is the rigorous fix and more machinery than this earns |
| Client derives "finished" from status | API returns `isTerminal` | Re-deriving the terminal set in TypeScript is how a client and server drift apart the moment a status is added |

Three worker behaviours that no verdict test reaches, and now have their own tests: an
already-judged submission is discarded rather than re-judged (reachable when a verdict was
written but the delete did not land); a message for a deleted row is dropped rather than
retried forever; and an unparseable message is discarded rather than left to re-trigger
KEDA indefinitely.

A local-only `judge submit` command was added so the queue-to-verdict path can be
exercised from a terminal. Acquiring a real Entra token needs an interactive browser
sign-in, which would otherwise make the one untested link also the hardest to try by hand.

### Phase 3, polish
- [ ] 10 to 15 problems
- [ ] Compile errors rendered with line numbers mapped back to the user's code (the harness is prepended, so **line offsets must be adjusted** or errors will point at the wrong lines)
- [ ] Submission history, per-problem solved state
- [ ] Editor niceties: keyboard submit, theme, reset to starter code
- [ ] README with an architecture diagram, the actual interview artifact

### Phase 4, explicitly deferred
Additional languages, leaderboard/contests, Redis, SignalR instead of polling, VNet + seccomp hardening of the judge, an AWS-side comparison.

---

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Judge compile-and-run harder than estimated | Blocks phase 2 | Phase 0 builds it first, locally, before any infrastructure exists |
| Cold start makes the demo feel broken | Bad first impression in the exact moment that matters | Communicate it in the UI. If unacceptable, set API `min_replicas = 1` for demo days and accept a few dollars |
| Multi-tenant token validation misconfigured | Interviewers cannot sign in, the worst possible failure | Use `Microsoft.Identity.Web`; test with a second Microsoft account early in phase 1 |
| Neon free tier autosuspends | ~500 ms cold connect | Acceptable. Retry-on-open in the Npgsql config |
| Terraform `azuread` provider needs directory permissions | Blocks phase 1 | Verify app-registration creation rights in the personal tenant during bootstrap |
| Line-number skew from the prepended harness | Confusing compile errors | Compile the harness as a *separate syntax tree*, not concatenated text, so Roslyn reports the submission's own line numbers |

---

## 11. Cost

Unchanged from the architecture doc at $0 to $5/month. GHCR keeps the registry at $0, the state storage account adds roughly $0.02, and Log Analytics ingestion remains the only realistic line item.
