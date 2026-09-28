# Marshal — single image (API only for now; the SPA is added by auth-web-shell).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Marshal.Api/Marshal.Api.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
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
