# Hardware Diagnostics & Serial Communication Protocol

## 1. Overview & Physical Topology

This document details the serial hardware communication architecture, the diagnostic tools, and the evidence-based testing protocol for the **WeighBridge Modern** system.

```text
Weighing Platform / Load Cells
          ↓ (Analog Load Cell Signals)
Weighing Indicator Terminal (Digital 7-Segment Readout)
          ↓ (RS-232 / UART Serial Output)
MA112 USB-to-UART Bridge
          ↓ (USB Packets)
Windows Virtual COM Port (e.g. COM3)
          ↓ (Asynchronous BaseStream.ReadAsync)
WeighBridge.SerialDiagnostic (Isolated Ground Truth)
          ↓ (Offline Binary Replay / Parser Proof)
WeighBridge Modern Application (Production WPF App)
```

### Key Hardware Facts
- **MA112 is a Transport Bridge:** The Megawin MA112 chip transfers raw serial data between UART and USB. It does not dictate the scale's framing or weight message protocol.
- **Deterministic Static Open Model:** The legacy application (`Weighbridge Entry Full Dongle.exe` / `Terminal.exe`) operates deterministically by opening the configured serial port once (`Opening Port` $\rightarrow$ `Opening Port Completed`) and continuously consuming the stream. Rapid baud-rate sweeping and cyclic reconnects can cause virtual COM driver instability and buffer corruption.
- **Evidence-Based Verification:** No code changes are made to the main application's transport or parser without physical `.bin` capture evidence proving the raw byte stream.

---

## 2. Standalone Diagnostic Tool: `WeighBridge.SerialDiagnostic`

The diagnostic tool is completely isolated from the main WPF application, DI containers, and background service managers. It communicates directly with `System.IO.Ports.SerialPort` via `BaseStream.ReadAsync()` to guarantee exact, unfiltered byte capture.

### 2.1 CLI Arguments Reference
| Argument | Type | Default | Description |
| :--- | :---: | :---: | :--- |
| `--port` | `string` | `COM3` | Serial port name (e.g. `COM1`, `COM3`) |
| `--baud` | `int` | `2400` | Baud rate (e.g. `2400`, `4800`, `9600`, `19200`, `38400`, `57600`, `115200`) |
| `--parity` | `string` | `None` | Parity: `None`, `Odd`, `Even`, `Mark`, `Space` |
| `--data` | `int` | `8` | Data bits: `7` or `8` |
| `--stop` | `string` | `One` | Stop bits: `One`, `OnePointFive`, `Two` |
| `--dtr` | `bool` | `true` | Enable/disable Data Terminal Ready (DTR) line |
| `--rts` | `bool` | `true` | Enable/disable Request To Send (RTS) line |
| `--handshake` | `string` | `None` | Handshake: `None`, `XOnXOff`, `RequestToSend`, `RequestToSendXOnXOff` |
| `--readtimeout`| `int` | `5000` | Serial read timeout in milliseconds |
| `--parser` | `flag` | `false` | Enable DelimitedFrameExtractor and GenericAsciiProtocolParser |
| `--replay` | `string` | `""` | Path to `.bin` capture file for offline replay mode |

### 2.2 Standardized Exit Codes
- `0`: Success / Normal completion (user requested exit with `Ctrl+C` or replay finished)
- `1`: Serial port open failure (port missing, in use by another program, or access denied)
- `2`: Invalid CLI configuration arguments
- `3`: Runtime I/O failure (unexpected disconnection, physical cable pull)
- `4`: Offline replay failure (file not found or corrupted)

---

## 3. Testing Protocols & CLI Commands

### Phase 1: Physical RAW Stream Capture
Run this command against the connected scale indicator to capture the raw stream:

```powershell
dotnet run --project src/WeighBridge.SerialDiagnostic --port COM3 --baud 2400
```

1. Allow the command to run for ~30 seconds while the scale is powered on.
2. Press `Ctrl+C` to cleanly exit.
3. Check the `Logs/` directory for the generated capture files:
   - `Logs/Capture_YYYYMMDD_HHMMSS.bin`: Exact, unadulterated raw bytes received from the OS.
   - `Logs/Capture_YYYYMMDD_HHMMSS.txt`: Human-readable formatted log with HEX dumps, ASCII rendering, timing deltas, and provenance header.

### Phase 2: Offline Binary Replay & Parser Verification
Run this command to test frame extraction and parser logic offline without physical hardware:

```powershell
dotnet run --project src/WeighBridge.SerialDiagnostic --replay Logs/Capture_YYYYMMDD_HHMMSS.bin --parser
```

**Acceptance Criteria:**
- Every complete expected weight frame parses correctly into a valid `WeightReading`.
- Any incomplete boundary fragments (at start/end of stream) are identified and accounted for.
- Any non-weight/status frames are logged with explicit, explainable reasons.
- Zero corrupted weights are silently accepted.

---

## 4. Diagnostic Evidence Table Schema

All physical test runs must be recorded in the following format to ensure complete traceability:

| Test Run | Configuration | Total Bytes | Null Bytes | Null % | Printable % | Read Count | Observed Delimiters | Status |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :--- | :--- |
| `Run-01` | COM3 / 2400 / 8N1 / DTR=1 / RTS=1 | — | — | — | — | — | `\r` (0x0D), `STX` (0x02) | Pending |

---

## 5. Anti-Fabrication & Truthfulness Protocol
1. **No Simulated Assumptions:** Real hardware behavior must never be guessed or simulated when physical hardware testing is available.
2. **Immutable Binary Capture:** The `.bin` file generated during Phase 1 is the sole ground truth. Parser unit tests must be written against captured binary fixtures.
3. **End-to-End Value Fidelity:** The numeric weight value displayed on the scale display must match the raw hex, the parsed reading, the stability evaluator, and the WPF HUD with zero rounding or locale divergence.
