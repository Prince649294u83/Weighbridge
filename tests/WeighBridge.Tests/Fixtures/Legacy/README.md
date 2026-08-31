# Legacy Golden Baseline Fixtures & SHA-256 Provenance

> [!WARNING]
> **IMMUTABILITY INVARIANT:**
> These files are byte-preserving baseline fixtures copied directly from the authoritative legacy codebase (`E:\Projects\Weighbridge Entry Dongle 2025\Weighbridge Entry Dongle`).
> **Do not edit these files manually.** Any modification requires explicit legacy-source re-verification and audit authorization.

---

## 1. Fixture Provenance & SHA-256 Checksums

| Fixture File | Legacy Source Path | Copied Date (UTC) | Line Endings | Encoding | Evidence Classification | SHA-256 Checksum |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Printing/Print_Ticket.txt` | `E:\...\Print_Ticket.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `9198883452C941AD069948EC8424FB43DB03D1B7C4201E6C7C0FBBCC8B73A93C` |
| `Printing/Print_Ticket_Advanced.txt` | `E:\...\Print_Ticket_Advanced.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `19C710C64A766577808E4E4D157FCFBF742F225439E77C116A2A1721AEFE4B1A` |
| `Printing/Print_Ticket_FCI.txt` | `E:\...\Print_Ticket_FCI.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `A8545DF1409700814D97F9DC862C3789B1A59A9B9EC23FC9DB1C7EF10D5B441F` |
| `Printing/Print_Ticket_Thermal.txt` | `E:\...\Print_Ticket_Thermal.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `2F2ADE54AD52BC545B3A65BF86AF057BD36901E387071BAFC7413D6B77365C69` |
| `Printing/Print_Ticket_Dot.txt` | `E:\...\Print_Ticket_Dot.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `E69882C138B741F2BD51C9BC0EE206247A6F2893950BE65346034EDB3D8200BA` |
| `Printing/Print_Ticket_A4.txt` | `E:\...\Print_Ticket_A4.txt` | 2026-08-31 | CRLF | Windows-1252 / ASCII | Legacy Verified | `555359DB3EB6AF3287B7CE82B9D3E210417E1BB291391F7167339D68501CA5F1` |
| `Messaging/SMS Format.txt` | `E:\...\SMS Format.txt` | 2026-08-31 | CRLF | UTF-8 / ASCII | Legacy Verified | `76A70566AA73312DB37D9CE975C444C55C9EEA2D82965EB760AFB5A15A0C1FF9` |

---

## 2. Usage in Automated Parity Tests

The test suite in `tests/WeighBridge.Tests/Printing/LegacyTemplateParityTests.cs` automatically asserts that the local fixture SHA-256 hashes match the provenance table above. If any fixture file is altered or corrupted, the automated test suite fails immediately.
