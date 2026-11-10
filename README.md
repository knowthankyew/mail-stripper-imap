# mail-stripper-imap 📬⚙️

> Protocol-Level **IMAP IDLE Quarantine Watcher**, Automated Folder Segregator & Sterile Opt-Out Daemon.

Built on Jeffrey Stedfast's industry-standard [`MailKit`](https://github.com/jstedfast/MailKit) library.  
Companion client to [`knowthankyew/mail-stripper`](https://github.com/knowthankyew/mail-stripper).

---

## 🎯 What It Does

1. 🔄 **Automated Mailbox Watching**: Listens on a dedicated IMAP folder (e.g. `@Safe-Unsubscribe` or `INBOX`) using real-time IMAP IDLE push notifications with exponential backoff auto-recovery.
2. 📁 **Automatic Mailbox Provisioning**: Checks for and auto-provisions the required folder hierarchy on startup (`@Safe-Unsubscribe`, `Archive`, `Quarantine`, `Audit`).
3. 💾 **100% Volatile RAM Streaming**: Downloads new messages directly into transient memory (`MemoryStream`). Zero bytes are ever written to disk or temporary files.
4. 🔬 **Deep Forensic Analysis**: Streams raw bytes to your local `mailStripper` engine (`http://localhost:5001/api/strip/file`) for RFC 8058, RFC 2369, DKIM, and receiving MX forensics.
5. 🚨 **Threat Segregation & Flagging**: Instantly tags messages with `$SuspiciousTrap` and moves deceptive phishing lures, punycode traps, and unaligned reconnaissance links to `@Safe-Unsubscribe/Quarantine`.
6. 🔒 **Optional Sterile Automated Opt-Out**: When explicitly enabled by the operator (`AutoExecuteSterileUnsubscribe: true`), fires an isolated zero-cookie RFC 8058 HTTP POST for verified newsletters, tags `$SafeUnsubscribed`, and moves them to `@Safe-Unsubscribe/Archive`.
7. 📑 **In-Mailbox Forensic Audit Reports**: Synthesizes cryptographic RFC 822 MIME summary reports into `@Safe-Unsubscribe/Audit` with defanged URLs, SHA-256 original message digest, MX verification, and sterile HTTP dispatch status, tagged with `$AuditLogged`.
8. 🩺 **Embedded Health Probes**: Lightweight HTTP listener providing `/healthz`, `/readyz`, and `/livez` endpoints reporting live connection state and metrics for container orchestrators.
9. 🛡️ **Supply Chain Assurance**: Central Package Management (CPM) with MSBuild fail-closed vulnerability gating (`NU1903/NU1904`).

---

## 🚀 Quickstart (Zero-Coder "Just Works")

Clone the repository and run:

```bash
./start.sh
```

`start.sh` builds the daemon, verifies central package management, runs the automated test suite, and launches the daemon in test configuration.

*(Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download) installed).*

---

## 🐳 Container Deployment

### Multi-Stage Docker Build

A hardened multi-stage Dockerfile is provided, publishing a minimal Alpine container executed under unprivileged non-root credentials (`USER $APP_UID`):

```bash
# Build container image
docker build -t mail-stripper-imap:latest .

# Run container with environment configuration
docker run -d \
  --name mail-stripper-imap \
  -p 8080:8080 \
  -e IMAP__HOST=imap.example.com \
  -e IMAP__PORT=993 \
  -e IMAP__USE_SSL=true \
  -e IMAP__USERNAME=user@example.com \
  -e IMAP__PASSWORD=secret \
  -e IMAP__MAIL_STRIPPER_URL=http://host.docker.internal:5001 \
  mail-stripper-imap:latest
```

### Docker Compose Orchestration

Run both `mail-stripper` (the analyzer engine) and `mail-stripper-imap` (the protocol watcher) together:

```bash
docker compose up -d
```

The composition exposes port `5001` for the forensic analyzer and port `8080` for watcher health probes.

---

## 🐧 Linux Systemd Service

For bare-metal Linux servers and VMs, a hardened systemd unit file is provided at `systemd/mail-stripper-imap.service`:

```bash
# 1. Install published binaries
sudo mkdir -p /opt/mail-stripper-imap
sudo cp -r /path/to/publish/* /opt/mail-stripper-imap/

# 2. Provision service user
sudo useradd -r -s /usr/sbin/nologin -d /opt/mail-stripper-imap mailstripper
sudo chown -R mailstripper:mailstripper /opt/mail-stripper-imap

# 3. Configure environment file
sudo mkdir -p /etc/mail-stripper-imap
sudo install -m 600 -o mailstripper -g mailstripper systemd/mail-stripper-imap.env.example /etc/mail-stripper-imap/env
# (Edit /etc/mail-stripper-imap/env with your IMAP credentials)

# 4. Install and enable systemd service
sudo cp systemd/mail-stripper-imap.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now mail-stripper-imap
sudo systemctl status mail-stripper-imap
```

The service runs under strict kernel sandboxing directives (`ProtectSystem=strict`, `NoNewPrivileges=true`, `PrivateTmp=true`, `ProtectHome=true`, `RestrictRealtime=true`).

---

## 🩺 Health Monitoring & Probes

The daemon provides an embedded HTTP health server (default port `8080`):

| Endpoint | Probe Type | Description | Healthy Response |
|:---------|:-----------|:------------|:-----------------|
| `/healthz` | Readiness / Health | Returns `200 OK` when connected to IMAP and active; returns `503 Service Unavailable` during reconnect/failure. | `{"status":"Healthy","connected":true,...}` |
| `/readyz` | Readiness | Alias for `/healthz` for Kubernetes readiness probes. | `200 OK` / `503 Unavailable` |
| `/livez` | Liveness | Returns `200 OK` as long as the process is alive. | `{"status":"Alive"}` |

### Sample Health Payload (`/healthz`)

```json
{
  "status": "Healthy",
  "connected": true,
  "healthy": true,
  "startedUtc": "2026-11-10T14:30:00+00:00",
  "uptimeSeconds": 1824.5,
  "lastConnectedUtc": "2026-11-10T14:30:02+00:00",
  "lastDisconnectedUtc": null,
  "lastMessageProcessedUtc": "2026-11-10T14:45:12+00:00",
  "messagesProcessedCount": 14,
  "lastError": null
}
```

---

## ⚙️ Configuration Reference

Configuration can be specified in `appsettings.json`, via standard hierarchical environment variables (`IMAP__*`), or flat environment variables (`IMAP_*`):

| Variable | Property | Default | Description |
|:---------|:---------|:--------|:------------|
| `IMAP__HOST` / `IMAP_HOST` | `Host` | `127.0.0.1` | Remote IMAP server hostname or IP |
| `IMAP__PORT` / `IMAP_PORT` | `Port` | `993` | IMAP port (993 for SSL/TLS, 143 for STARTTLS) |
| `IMAP__USE_SSL` / `IMAP_USE_SSL` | `UseSsl` | `true` | Enables implicit TLS/SSL connection |
| `IMAP__USERNAME` / `IMAP_USERNAME` | `Username` | `""` | IMAP account username / email address |
| `IMAP__PASSWORD` / `IMAP_PASSWORD` | `Password` | `""` | IMAP account password or app-specific token |
| `IMAP__MONITORED_FOLDER` | `MonitoredFolder` | `@Safe-Unsubscribe` | Mailbox folder to monitor via IDLE |
| `IMAP__ARCHIVE_FOLDER` | `ArchiveFolder` | `@Safe-Unsubscribe/Archive` | Target folder for successfully processed opt-outs |
| `IMAP__QUARANTINE_FOLDER` | `QuarantineFolder` | `@Safe-Unsubscribe/Quarantine` | Target folder for malicious / deceptive lures |
| `IMAP__AUDIT_FOLDER` | `AuditFolder` | `@Safe-Unsubscribe/Audit` | Target folder for cryptographic audit reports |
| `IMAP__MAIL_STRIPPER_URL` | `MailStripperUrl` | `http://localhost:5001` | URL of local `mailStripper` forensic analyzer |
| `IMAP__TRUSTED_AUTHSERV_ID` | `TrustedAuthservId` | `null` | Optional receiving MX authserv-id boundary |
| `IMAP__AUTO_EXECUTE_STERILE_UNSUBSCRIBE` | `AutoExecuteSterileUnsubscribe` | `false` | When `true`, automatically dispatches sterile HTTP POST |
| `IMAP__MAX_MESSAGE_BYTE_SIZE` | `MaxMessageByteSize` | `15728640` | Maximum message size budget (default 15 MB) |
| `IMAP__RECONNECT_INITIAL_DELAY_SECONDS` | `ReconnectInitialDelaySeconds` | `2` | Initial backoff retry delay in seconds |
| `IMAP__RECONNECT_MAX_DELAY_SECONDS` | `ReconnectMaxDelaySeconds` | `300` | Maximum capped backoff retry delay in seconds |
| `IMAP__HEALTH_CHECK_ENABLED` | `HealthCheckEnabled` | `true` | Enables embedded HTTP health server |
| `IMAP__HEALTH_CHECK_PORT` / `HEALTH_PORT` | `HealthCheckPort` | `8080` | Port for `/healthz` and `/livez` probes |
| `IMAP__HEALTH_CHECK_HOST` | `HealthCheckHost` | `127.0.0.1` | Host binding for health listener (`127.0.0.1` for loopback, `*` for containers) |

---

## 🧪 Testing & Verification

- **xUnit Automated Tests**:
  ```bash
  dotnet test
  ```
- **Architectural Claims Audit**:
  ```bash
  node scripts/audit-claims.mjs
  ```

---

## 🛡️ Security & Volatile RAM Guarantees

- **Zero Disk Writes**: All IMAP operations use `MemoryStream`. No temporary `.eml` files on disk.
- **Sterile POST Execution**: Disables ambient cookies (`UseCookies = false`), disables redirects (`AllowAutoRedirect = false`), and immediately drops response payload streams.
- **Fail-Closed Dependency Audit**: MSBuild treats `NU1901`-`NU1904` package vulnerabilities as fatal build errors.
- **Unprivileged Non-Root Execution**: Container runs with non-root user (`$APP_UID`), and systemd unit drops all privileges.

---

## 📄 License & Attributions

- [MIT License](LICENSE)
- [Third-Party Disclosures](NOTICE.md)
