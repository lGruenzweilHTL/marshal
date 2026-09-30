# Marshal executor host — a separate image, never part of the Marshal container.
# Build from the repository root: docker build -f src/Marshal.ExecutorHost/Dockerfile -t marshal-executor-host .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY src/Marshal.ExecutorHost/ src/Marshal.ExecutorHost/
RUN dotnet publish src/Marshal.ExecutorHost/Marshal.ExecutorHost.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# git for the per-session worktrees; the docker CLI talks to the rootless (or Sysbox) daemon given by DOCKER_HOST.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /app .
# The sandbox containers mount worktrees and git directories by path on the Docker host, so /repos and /worktrees
# must be bind mounts with the same paths on the host and in this container.
EXPOSE 8080
USER app
ENTRYPOINT ["/app/marshal-executor-host"]
