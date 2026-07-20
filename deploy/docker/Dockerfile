# syntax=docker/dockerfile:1
# Single parameterized Dockerfile for all 4 ASP.NET Core hosts. Build with:
#   docker build -f deploy/docker/Dockerfile \
#     --build-arg HOST_PROJECT=Hosts/Wbskt.Auth.Host/Wbskt.Auth.Host.csproj \
#     --build-arg HOST_DLL=Wbskt.Auth.Host.dll \
#     .
# (build context = repo root; see deploy/compose/docker-compose.yml for the per-service args)

ARG DOTNET_SDK_VERSION=10.0
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
ARG HOST_PROJECT
WORKDIR /src
COPY . .
# sharing=locked: all 4 host images build from this same Dockerfile and share this cache mount.
# NuGet's package extraction isn't safe for concurrent writers to the same global packages folder
# (observed firsthand: parallel `docker compose build` corrupted the shared cache with a
# "Could not find file '.../cronos/.../<tmp>'" error) - locked serializes access instead of
# letting BuildKit run them concurrently against the same directory.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "${HOST_PROJECT}" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS final
# curl is needed for the compose-level HTTP healthchecks against /healthz.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# The host .csproj files copy Config\*.json into the publish output at content root, which loads
# at HIGHER precedence than the /Config bind mount (see SharedConfigurationExtension.cs). Remove
# them so the mounted /Config (and env vars) are what actually apply in the container.
RUN rm -f jwt.json rabbitmq.json serilog.json connectionstrings.json workflow-engine.json

ARG HOST_DLL
ENV HOST_DLL=${HOST_DLL}
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$HOST_DLL\""]
