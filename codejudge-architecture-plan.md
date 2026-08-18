# CodeJudge — Architecture & Build Plan

A portfolio project: a LeetCode-style platform where users solve coding problems and submit code that runs against test cases in an isolated sandbox. Built to demo well in interviews and to double as your own coding-practice tool, at near-zero hosting cost.

## 1. Goals

- Demonstrate real system design judgment: sandboxed untrusted code execution, async job processing, IaC, CI/CD — the things that come up in interviews.
- Cost-optimized for a project that's idle most of the time between demos and practice sessions (target: $0–5/month).
- Built with tools you already use professionally, so the code itself is also a portfolio piece.

## 2. Tech stack

| Layer | Choice | Notes |
|---|---|---|
| API | .NET 10 Web API (C#) | Consider Clean Architecture + MediatR (CQRS) — matches your Polaris work and gives you a consistent story across your portfolio. Your call. |
| Frontend | React + TypeScript | Monaco editor for the code input |
| Auth | Microsoft Entra ID | MSAL.js on the frontend, JWT bearer validation on the API. See section 4 for App Registration setup. |
| Judge worker | C#/.NET only for v1 | Runs on the .NET 10 SDK image, triggered by Azure Container Apps Jobs |
| Database | Neon (managed Postgres) | Free tier, outside Azure |
| IaC | Terraform | All Azure resources defined as code |
| CI/CD | GitHub Actions | Build/test/deploy pipelines |

## 3. High-level architecture

```
Client tier        Static Web Apps (free tier) — React/TS + Monaco
                     MSAL.js handles Entra ID sign-in
        |
        v
Application tier    Container Apps (Consumption, scale-to-zero) — .NET 10 Web API
                     Validates Entra ID bearer tokens on every request
                     Azure Storage Queue — submission job queue
        |
        v
Judge tier           Container Apps Jobs (event-driven, scale-to-zero)
                     .NET 10 SDK image, one container execution per
                     submission, isolated, torn down after each run
        |
        v
Data tier            Neon Postgres (external) — Problems, TestCases,
                     Submissions, Users
```

Every compute tier bills per-second and scales to zero when idle — there's no AKS node pool or other always-on compute in this design.

## 4. Azure resources (Terraform-managed)

| Resource | Terraform type | Purpose | Cost driver |
|---|---|---|---|
| Resource group | `azurerm_resource_group` | Container for everything | Free |
| Static Web App | `azurerm_static_web_app` | Hosts the React app | Free tier |
| Log Analytics workspace | `azurerm_log_analytics_workspace` | Required by the Container Apps environment; also your log sink | Free tier covers demo volume |
| Container Apps environment | `azurerm_container_app_environment` | Shared environment for the API app and the judge job | Free (the apps/jobs inside it are what's billed) |
| Container App (API) | `azurerm_container_app` | Runs the .NET Web API, min replicas = 0 | Free tier grant (180K vCPU-sec/mo) |
| Storage account + queue | `azurerm_storage_account` + `azurerm_storage_queue` | Submission job queue | Pennies |
| Container Apps Job (judge) | `azurerm_container_app_job` | Event-driven, triggered by queue depth via KEDA | Free tier grant covers demo volume |
| Key Vault | `azurerm_key_vault` | Neon connection string, JWT signing key | Near-free at low request volume |
| User-assigned managed identity | `azurerm_user_assigned_identity` | Lets the Container App/Job read Key Vault without embedded credentials | Free |
| Entra ID App Registrations (x2) | `azuread_application` + `azuread_service_principal` (via the `azuread` Terraform provider, alongside `azurerm`) | One registration exposing an API scope (for the .NET API), one public-client registration for the React SPA that requests that scope | Free |

Configure the App Registrations as **multi-tenant, accounts in any organization + personal Microsoft accounts** rather than restricting sign-in to your own tenant — otherwise interviewers trying the live demo won't be able to sign in with their own Microsoft account.

**Deliberately excluded for v1** (add later if the project's story calls for it): Container Registry (reference public images like `python:3.12-slim` directly instead), Redis, Web PubSub/SignalR (poll instead), VNet/NSG subnet isolation.

## 5. Data model (Neon Postgres)

- **Users** — id, entra_object_id (from the token's `oid` claim), email, display_name, created_at
- **Problems** — id, title, slug, difficulty, statement_md, constraints, created_at
- **TestCases** — id, problem_id (FK), input, expected_output, is_hidden
- **Submissions** — id, user_id (FK), problem_id (FK), language, code, status (`Queued`/`Running`/`Accepted`/`WrongAnswer`/`TimeLimitExceeded`/`RuntimeError`), runtime_ms, memory_kb, created_at, completed_at

## 6. Submission pipeline

1. User signs in via MSAL.js (Entra ID); the SPA holds an access token for the API's scope.
2. User writes code in the Monaco editor and submits.
3. React app calls the .NET Web API with the token as a bearer header.
4. API validates the token, inserts a `Submission` row (`status = Queued`), and pushes a message to the Storage Queue.
5. The queue message triggers a Container Apps Job execution (KEDA scaler watches queue depth).
6. The job runs the submitted C# against each test case with a CPU/memory/time limit, and captures stdout/exit code.
7. The job writes the verdict, runtime, and memory back to the `Submissions` row.
8. The React app polls the submission status endpoint until it's no longer `Queued`/`Running`.

### Executing untrusted C#

Two realistic options for step 6, worth prototyping both before committing:

- **Roslyn scripting** (`Microsoft.CodeAnalysis.CSharp.Scripting`) — evaluates the submission in-process inside the already-sandboxed job container. Fast (no build step per submission), but you're relying on the container boundary alone for isolation rather than a fresh process per run, and you'll need a harness that maps a LeetCode-style method signature (e.g. a `Solution` class with a `TwoSum` method) to something `CSharpScript.EvaluateAsync` can invoke and feed test-case input into.
- **Scaffolded project + `dotnet build`/`dotnet run`** — closer to what other online judges do for compiled languages: drop the submission into a pre-cached console project template, build, run as a separate process. Slower per submission (JIT/build overhead) unless the image pre-warms the NuGet cache, but isolation is cleaner since the untrusted code runs as its own process, and it's a more familiar mental model to explain in an interview.

Start with Roslyn scripting for speed of development; the scaffolded-project approach is the natural "if I had more time" answer if the isolation question comes up.

## 7. Suggested repo structure

```
/apps
  /api      .NET 10 Web API
  /web      React + TypeScript frontend
  /judge    Judge worker source + one Dockerfile per language runtime
/infra
  /terraform
/.github
  /workflows
```

## 8. CI/CD (GitHub Actions)

- `ci-api.yml` — build, test the .NET API on every PR
- `ci-web.yml` — build, lint, test the React app on every PR
- `ci-judge.yml` — build the judge worker image(s) on every PR
- `cd-infra.yml` — `terraform plan` on PR, `terraform apply` on merge to `main`
- `cd-deploy.yml` — deploy the API container, judge job definition, and Static Web App after infra apply succeeds

## 9. Build phases

1. **Skeleton + auth** — Terraform for the resource group, Static Web App, Container Apps environment, API container app, Neon connection, and both Entra ID App Registrations. API returns a hardcoded problem list behind an authenticated endpoint; React handles MSAL.js sign-in and renders the list. No judging yet.
2. **Judge pipeline** — Storage Queue + Container Apps Job wired up for C#. Get the full submit → queue → execute → verdict round trip working end to end, starting with the Roslyn scripting approach.
3. **Polish** — more problems, better error messages for compile/runtime failures, UI polish on the editor and results view.
4. **Future ideas (explicitly out of scope for v1)** — additional languages, a leaderboard or contest mode, Redis caching, or a side-by-side AWS version of the judge tier for cloud-comparison talking points.

## 10. Estimated cost

$0–5/month at demo-scale usage — the only line item likely to show up on a bill is Log Analytics ingestion if you're generous with logging, and even that stays inside the free monthly grant for this volume.

## 11. v1 scope decisions

- **Languages**: C#/.NET only for v1.
- **Auth**: Microsoft Entra ID from the start, configured for multi-tenant + personal accounts so anyone can sign in.
- **Feature scope**: core judging only — no contests or leaderboard in v1.
