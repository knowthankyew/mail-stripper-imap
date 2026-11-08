# mail-stripper-imap 📬⚙️

> Protocol-Level **IMAP IDLE Quarantine Watcher**, Automated Folder Segregator & Sterile Opt-Out Daemon.

Built on Jeffrey Stedfast's industry-standard [`MailKit`](https://github.com/jstedfast/MailKit) library.  
Companion client to [`knowthankyew/mail-stripper`](https://github.com/knowthankyew/mail-stripper).

---

## 🎯 What It Does

1. 🔄 **Automated Mailbox Watching**: Listens on a dedicated IMAP folder (e.g. `@Safe-Unsubscribe` or `INBOX`) using real-time IMAP IDLE push notifications.
2. 💾 **100% Volatile RAM Streaming**: Downloads new messages directly into transient memory (`MemoryStream`). Zero bytes are ever written to disk or temporary files.
3. 🔬 **Deep Forensic Analysis**: Streams raw bytes to your local `mailStripper` engine (`http://localhost:5001/api/strip/file`) for RFC 8058, RFC 2369, DKIM, and receiving MX forensics.
4. 🚨 **Threat Segregation**: Instantly flags and moves deceptive phishing lures, punycode traps, and unaligned reconnaissance links to `@Safe-Unsubscribe/Quarantine`.
5. 🔒 **Optional Sterile Automated Opt-Out**: When explicitly enabled by the operator (`AutoExecuteSterileUnsubscribe: true`), fires an isolated zero-cookie RFC 8058 HTTP POST for verified newsletters, then moves them to `@Safe-Unsubscribe/Archive`.
6. 🛡️ **Supply Chain Assurance**: Central Package Management (CPM) with MSBuild fail-closed vulnerability gating (`NU1903/NU1904`).

---

## 🚀 Quickstart (Zero-Coder "Just Works")

Clone the repository and run:

```bash
./start.sh
```

`start.sh` builds the daemon, verifies central package management, runs the automated test suite, and launches the daemon in test configuration.

*(Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download) installed).*

---

## ⚙️ Configuration

Configure via `appsettings.json` or standard environment variables (`IMAP__*`):

```json
{
  "Imap": {
    "Host": "imap.example.com",
    "Port": 993,
    "UseSsl": true,
    "Username": "user@example.com",
    "Password": "app-specific-password",
    "MonitoredFolder": "@Safe-Unsubscribe",
    "ArchiveFolder": "@Safe-Unsubscribe/Archive",
    "QuarantineFolder": "@Safe-Unsubscribe/Quarantine",
    "AuditFolder": "@Safe-Unsubscribe/Audit",
    "MailStripperUrl": "http://localhost:5001",
    "TrustedAuthservId": "mx.google.com",
    "AutoExecuteSterileUnsubscribe": false,
    "MaxMessageByteSize": 15728640
  }
}
```

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
- **Fail-Closed Dependency Audit**: MSBuild treats `NU1903` and `NU1904` package vulnerabilities as fatal build errors.

---

## 📄 License & Attributions

- [MIT License](LICENSE)
- [Third-Party Disclosures](NOTICE.md)
