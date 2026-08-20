# Build context is the REPOSITORY ROOT, not this directory:
#   docker build -f apps/judge/CodeJudge.Judge/Dockerfile -t codejudge-judge .
# The build needs global.json, Directory.Build.props and Directory.Packages.props, all of
# which live above this file.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the build configuration and every csproj first, then restore. This layer only
# invalidates when a dependency actually changes, so ordinary source edits reuse the
# cached restore instead of re-downloading the world.
COPY global.json Directory.Build.props Directory.Packages.props ./

# Runner.targets carries the ProjectReference to the runner, and CodeJudge.Judge.csproj
# imports it. Without this line restore fails before it reads a single source file.
COPY apps/judge/Runner.targets apps/judge/

COPY apps/api/CodeJudge.Domain/CodeJudge.Domain.csproj                 apps/api/CodeJudge.Domain/
COPY apps/api/CodeJudge.Application/CodeJudge.Application.csproj       apps/api/CodeJudge.Application/
COPY apps/api/CodeJudge.Infrastructure/CodeJudge.Infrastructure.csproj apps/api/CodeJudge.Infrastructure/
COPY apps/judge/CodeJudge.Judge/CodeJudge.Judge.csproj                 apps/judge/CodeJudge.Judge/
COPY apps/judge/CodeJudge.Judge.Runner/CodeJudge.Judge.Runner.csproj   apps/judge/CodeJudge.Judge.Runner/

RUN dotnet restore apps/judge/CodeJudge.Judge/CodeJudge.Judge.csproj

COPY apps/api/ apps/api/
COPY apps/judge/ apps/judge/

RUN dotnet publish apps/judge/CodeJudge.Judge/CodeJudge.Judge.csproj \
    -c Release \
    -o /app \
    --no-restore

# The runner is published separately, and this is the step that is easy to leave out.
#
# Runner.targets copies the runner into $(OutDir) AfterTargets="Build", and `dotnet
# publish` never consumes $(OutDir). A single publish therefore yields an image that
# starts cleanly, claims a message, and only then throws FileNotFoundException from
# RunnerLocator. Publishing it into ./runner puts it exactly where RunnerLocator looks
# (AppContext.BaseDirectory + "runner"), with its own runtimeconfig.json and deps.json,
# which `dotnet exec` requires.
RUN dotnet publish apps/judge/CodeJudge.Judge.Runner/CodeJudge.Judge.Runner.csproj \
    -c Release \
    -o /app/runner \
    --no-restore

# `runtime`, not `aspnet`: the judge is a console app that serves no HTTP.
#
# Framework-dependent and never trimmed, deliberately. CompilationService builds its
# Roslyn references from TRUSTED_PLATFORM_ASSEMBLIES, which is the set of assemblies this
# process actually started with. Trimming or self-contained publishing prunes that set,
# and the failure is not a missing file: it is submissions failing to compile against
# types the allowlist expects to be there.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI/Kerberos when it opens a connection. Without this library it
# writes "Cannot load library libgssapi_krb5.so.2" to stderr every single time and then
# carries on with password authentication. Functionally harmless, but every job execution
# would open its log with what reads as an error, and this costs about two megabytes.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# APP_UID is a non-root user defined by the base image. This matters more here than on
# the API: this container exists to execute untrusted code. It is not a sandbox on its
# own (see section 5 of the build plan), but root inside the container would remove one
# of the few boundaries that is actually free.
USER $APP_UID

# One execution is one unit of work, which is what a Container Apps Job gives us: KEDA
# starts the container, it claims a single message, judges it, and exits. `worker --once`
# is that shape. Override the command for anything else, e.g. `seed`.
ENTRYPOINT ["dotnet", "CodeJudge.Judge.dll"]
CMD ["worker", "--once"]
