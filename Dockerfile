# The app is published on the host (mise run publish --rid fdd) and copied in,
# so the image carries the same bits — and the same stamped version — as the
# release archives. See mise-tasks/container.
FROM mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1
WORKDIR /app
COPY artifacts/fdd .

VOLUME ["/data"]
WORKDIR /data

ENTRYPOINT ["dotnet", "/app/RustDaemon.Cli.dll"]
