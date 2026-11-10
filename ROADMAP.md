# Roadmap: mail-stripper-imap — Protocol-Level IMAP IDLE Quarantine Watcher

> **Repository**: [`knowthankyew/mail-stripper-imap`](https://github.com/knowthankyew/mail-stripper-imap)  
> **Governance Scope**: Headless .NET 10 Daemon built on Jeffrey Stedfast's [`MailKit`](https://github.com/jstedfast/MailKit)  
> **Backend Integration**: Local `mailStripper` Ingestion Plane (`http://localhost:5001/api/strip/file`)  
> **Governance Standard**: 100% Volatile RAM Ingestion · Zero Disk Writes · Central Package Management · Fail-Closed NuGet Audit · Sterile RFC 8058 Execution

---

## 1. Executive Context for Incoming AI Agents

This repository serves as **Companion B** to [`knowthankyew/mail-stripper`](https://github.com/knowthankyew/mail-stripper).

### Why a Separate Repository Was Established
Under portfolio governance boundaries:
1. `mailStripper` operates under an uncompromising air-gap (`connect-src 'self' http://localhost:8000;` only, 100% volatile in-memory). It is an ingestion plane and forensic analyzer, not an active network protocol client.
2. `mail-stripper-imap` connects directly to remote IMAP servers over TLS to monitor mailboxes, segregate threat traps, and optionally automate opt-out executions on behalf of a user.

---

## 2. Master Milestone Grid

| Milestone | Target | Description | Status |
|:----------|:------:|:------------|:------:|
| **Milestone 1** | `v0.1.0` | **Scaffold & In-Memory Pipeline**: .NET 10 solution, CPM, MailKit integration, streaming client, sterile dispatcher, unit tests, claims linter | 🟢 **Implemented** |
| **Milestone 2** | `v0.2.0` | **Folder Quarantine & Segregation**: Automatic creation of `@Safe-Unsubscribe` subfolders, flag tagging (`$SafeUnsubscribed`, `$SuspiciousTrap`), moving messages | 🟢 **Implemented** |
| **Milestone 3** | `v0.3.0` | **In-Mailbox Audit Reports**: Generates cryptographic audit summary MIME messages inside `@Safe-Unsubscribe/Audit` with forensic verdicts | 📋 **Planned** |
| **Milestone 4** | `v0.4.0` | **Containerized Microservice & Systemd Unit**: Docker container definition, Linux systemd unit, healthcheck endpoint | 📋 **Planned** |

---

## 3. Detailed Milestone Specifications

### Milestone 1: Core Daemon Scaffold & In-Memory Pipeline (`v0.1.0`)
- [x] **Project Structure & CPM**:
  - `Directory.Packages.props` with pinned `MailKit` (4.18.2), `MimeKit` (4.18.2), `Microsoft.Extensions.Hosting` (10.0.14).
  - `Directory.Build.props` enforcing fail-closed `NU1903/NU1904` audit warnings.
- [x] **Streaming mailStripper Client (`MailStripperClient.cs`)**:
  - Streams raw message bytes over `MultipartFormDataContent` to `http://localhost:5001/api/strip/file`.
  - Passes optional `TrustedAuthservId` to verify receiving MX boundaries.
- [x] **Sterile RFC 8058 Dispatcher (`SterileUnsubscribeDispatcher.cs`)**:
  - Enforces `UseCookies = false` and `AllowAutoRedirect = false`.
  - Immediately disposes response stream upon receiving status.
  - Refuses non-https targets and rejects messages carrying poison signals.
- [x] **IMAP Watcher Service (`ImapWatcherService.cs`)**:
  - Connects to IMAP server with TLS.
  - Downloads message stream into volatile `MemoryStream` (zero disk writes).
  - Dispatches to `mailStripper` and categorizes verdicts.
- [x] **Testing & Verification**:
  - xUnit unit tests verifying sterile dispatcher and client streaming.
  - Automated architectural claims audit (`scripts/audit-claims.mjs`).

### Milestone 2: Folder Quarantine & Segregation (`v0.2.0`)
- [x] **Mailbox Provisioning**: Automatically check for and create `@Safe-Unsubscribe` folder hierarchy on startup if missing:
  - `@Safe-Unsubscribe` (Monitored Inbox)
  - `@Safe-Unsubscribe/Archive` (Successfully processed)
  - `@Safe-Unsubscribe/Quarantine` (Phishing / Recon traps)
  - `@Safe-Unsubscribe/Audit` (Audit logs)
- [x] **Message Flag Tagging**: Apply IMAP custom keywords (`$SafeUnsubscribed`, `$SuspiciousTrap`, `$AuditLogged`).
- [x] **Batch Recovery**: Reconnect retry loop with exponential backoff on network drop.

### Milestone 3: In-Mailbox Audit Reports (`v0.3.0`)
- [ ] **MIME Audit Generator**: Synthesize a clean, defanged RFC 822 summary message containing:
  - Original sender and subject.
  - Forensic risk score (0-100) and classification.
  - Matched receiving MX and DKIM domain alignments.
  - Exact timestamp and sterile HTTP status returned by the target ESP.
- [ ] **Append to Audit Mailbox**: Append the generated audit MIME message directly to `@Safe-Unsubscribe/Audit`.

### Milestone 4: Production Deployment & Containerization (`v0.4.0`)
- [ ] **Multi-Stage Dockerfile**: Minimal Chiseled Ubuntu / Alpine container running non-root daemon.
- [ ] **Environment Variable Mapping**: Full configuration via `IMAP__HOST`, `IMAP__USERNAME`, `IMAP__PASSWORD`, etc.
- [ ] **Health Monitoring**: Lightweight HTTP endpoint `/healthz` for container orchestrator probes.

---

## 4. Non-Negotiable Technical Guardrails & Edge Cases

### Edge Case 1: 100% Volatile RAM Ingestion
- **Issue**: Accidental use of `Path.GetTempFileName()` or `FileStream` leaks user email content to persistent storage.
- **Rule**: All message content must be downloaded into in-memory `MemoryStream` buffers and immediately garbage collected.

### Edge Case 2: Accidental Automated Poison Execution
- **Issue**: If a malicious email carries a deceptive `List-Unsubscribe` header pointing to an attacker beacon, firing a request confirms active victim status.
- **Rule**: `SterileUnsubscribeDispatcher` must reject any candidate if `report.PoisonSignals.Count > 0` or if the classification is `PoisonOrReconTrap`.

### Edge Case 3: Reconnection During IMAP IDLE
- **Issue**: Mail servers close idle connections after 10–30 minutes or on network jitter.
- **Rule**: `ImapWatcherService` must handle `ImapClient.IdleAsync` with periodic keepalive pings (max 9 minutes) and auto-reconnect on socket drop.
