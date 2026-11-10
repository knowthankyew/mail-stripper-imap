# syntax=docker/dockerfile:1
# ── Stage 1: Build & Publish Backend ──────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /app

# Copy solution CPM definitions and project file for optimized layer caching
COPY Directory.Build.props Directory.Packages.props ./
COPY MailStripper.Imap/*.csproj MailStripper.Imap/
RUN dotnet restore MailStripper.Imap/MailStripper.Imap.csproj

# Copy source code and publish in Release mode
COPY MailStripper.Imap/ MailStripper.Imap/
WORKDIR /app/MailStripper.Imap
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Hardened Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app

# Install wget for lightweight container health checks
RUN apk add --no-cache wget

# Copy published application from build stage
COPY --from=build /app/publish .

# Application health and networking configuration
ENV IMAP__HEALTH_CHECK_PORT=8080
ENV IMAP__HEALTH_CHECK_HOST=*

# Run as non-root user ($APP_UID is defined in official .NET Alpine images as UID 1654)
USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "MailStripper.Imap.dll"]
