# The image testament's runners run Cordyceps' unit tests in (.testament.toml).
# The test project links Core sources only, so it needs the .NET 8 SDK and no Rhino.
# Pinned by digest (the multi-arch manifest list) so every rebuild gets the same base;
# move the pin on purpose.
FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:341aa6e8bbaceb1194732328ca27c15bfa9c531687db83a66af3c0c1833f5534

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# testament refuses an image that runs as root; the SDK image ships the non-root `app` user.
USER app
