# Multi-stage build: the SDK image (build tools + runtime) is much larger than needed
# just to run the published app, so only the aspnet runtime image ships in the final layer.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, in its own layer, so editing game source doesn't invalidate the
# (much slower) NuGet restore on every rebuild.
COPY MudServer/MudServer.csproj MudServer/
RUN dotnet restore MudServer/MudServer.csproj

COPY MudServer/ MudServer/
RUN dotnet publish MudServer/MudServer.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# The HEALTHCHECK below polls /healthz with curl - not present in the slim runtime image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Don't run as root. Server.userFilePath resolves to $HOME/.local/share/winspod (.NET's
# LocalApplicationData under Linux) - HOME is set explicitly here so that path is
# predictable for the VOLUME declaration below, rather than depending on whatever HOME
# a container's default user happens to have.
RUN useradd --create-home --home-dir /home/mudserver --shell /usr/sbin/nologin mudserver
ENV HOME=/home/mudserver

WORKDIR /app
COPY --from=build --chown=mudserver:mudserver /app .

# The data directory must exist (owned by mudserver) BEFORE the VOLUME instruction below -
# VOLUME creates its mount point owned by root if the path doesn't already exist in the
# image, which then denies writes from the non-root user this container actually runs as.
RUN mkdir -p /home/mudserver/.local/share/winspod \
    && chown -R mudserver:mudserver /home/mudserver/.local/share/winspod

USER mudserver

# Player/room/log data - mount a volume here to persist it across container recreation.
VOLUME ["/home/mudserver/.local/share/winspod"]

# 4000: telnet game port (always listening). 4001: optional JSON API/healthz, only bound
# when MUD_HTTP_ENABLED=true. 4443: optional TLS-wrapped telnet, only bound when
# MUD_TLS_ENABLED=true - additional to 4000, not a replacement (see Program.cs's
# ApplyEnvironmentOverrides and Server.cs's ListenTlsAsync).
EXPOSE 4000 4001 4443

# Only meaningful with MUD_HTTP_ENABLED=true - without the API listening, there's no HTTP
# endpoint for curl to poll, so this reports healthy unconditionally in that mode (the
# process being alive at all is still checked implicitly by Docker's own restart policy).
# Explicit `sh -c '...'` (not a bare `[ ... ]`) - a HEALTHCHECK/CMD value starting with a
# literal '[' is parsed as Docker's JSON exec-form and fails the build if it isn't valid
# JSON, rather than falling back to shell form. Spelling it this way sidesteps that.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD sh -c 'test "$MUD_HTTP_ENABLED" != "true" || curl -f http://localhost:4001/healthz'

ENTRYPOINT ["dotnet", "MudServer.dll"]
