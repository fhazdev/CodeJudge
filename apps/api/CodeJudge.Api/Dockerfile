# Build context is the REPOSITORY ROOT, not this directory:
#   docker build -f apps/api/CodeJudge.Api/Dockerfile -t codejudge-api .
# The build needs global.json, Directory.Build.props and Directory.Packages.props, all of
# which live above this file.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the build configuration and every csproj first, then restore. This layer only
# invalidates when a dependency actually changes, so ordinary source edits reuse the
# cached restore instead of re-downloading the world.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY apps/api/CodeJudge.Domain/CodeJudge.Domain.csproj                 apps/api/CodeJudge.Domain/
COPY apps/api/CodeJudge.Application/CodeJudge.Application.csproj       apps/api/CodeJudge.Application/
COPY apps/api/CodeJudge.Infrastructure/CodeJudge.Infrastructure.csproj apps/api/CodeJudge.Infrastructure/
COPY apps/api/CodeJudge.Api/CodeJudge.Api.csproj                       apps/api/CodeJudge.Api/

RUN dotnet restore apps/api/CodeJudge.Api/CodeJudge.Api.csproj

COPY apps/api/ apps/api/

RUN dotnet publish apps/api/CodeJudge.Api/CodeJudge.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Container Apps routes to whatever this listens on; 8080 is the convention for a
# non-root container, since binding below 1024 would require privileges we do not want.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .

# APP_UID is defined by the base image (a non-root user). Running as root inside a
# container that terminates public traffic buys nothing and costs a layer of defence.
USER $APP_UID

ENTRYPOINT ["dotnet", "CodeJudge.Api.dll"]
