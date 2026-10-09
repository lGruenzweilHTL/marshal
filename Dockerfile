# Marshal — single image: API host + SPA (served from wwwroot).

# 1. Publish the host and export its OpenAPI document (no running server needed).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY src/ src/
# The code highlighter's language definitions are shared with the SPA; Marshal.Artifacts embeds them.
COPY web/src/artifacts/highlighting/languages.json web/src/artifacts/highlighting/
RUN dotnet publish src/Marshal.Api/Marshal.Api.csproj --configuration Release --output /app
RUN MARSHAL__Paths__Config=/tmp/marshal/config MARSHAL__Paths__Data=/tmp/marshal/data \
    /app/marshal --export-openapi /openapi/v1.json

# 2. Build the SPA against that document: a contract change the SPA does not handle fails here.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
COPY --from=build /openapi/v1.json openapi/v1.json
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# git (and ssh for deploy keys) mirrors a remote notes repository into /data/notes.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
# `marshal tokens create ...` works inside the container (docker exec <container> marshal tokens ...).
RUN ln -s /app/marshal /usr/local/bin/marshal \
    && mkdir -p /config /data \
    && chown app:app /data
ENV MARSHAL__Paths__Config=/config \
    MARSHAL__Paths__Data=/data \
    MARSHAL__Paths__Secrets=/run/secrets
VOLUME ["/data"]
EXPOSE 8080
USER app
ENTRYPOINT ["/app/marshal"]
