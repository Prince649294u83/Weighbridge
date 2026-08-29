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

All physical test runs and replays are recorded below with complete traceability:

| Test Run | Configuration | Total Bytes | Null Bytes | Frames Extracted | Frames Parsed | Frame Delimiter | Status |
| :--- | :--- | :---: | :---: | :---: | :---: | :--- | :--- |
| `Phys-01` | COM3 / 2400 / 8N1 / DTR=0 / RTS=0 | 0 | 0 | 0 | 0 | None | Failed (Win32 Error 31) |
| `Phys-02` | COM3 / 2400 / 8N1 / DTR=1 / RTS=1 | 1,085 | 120 | 119 | 119 | `[` (0x5B) ... `\0` (0x00) | **PROVEN (100% Success)** |
| `Replay-01` | SerialCapture_2026-08-29_1254.bin | 1,085 | 120 | 119 | 119 | `[` (0x5B) ... `\0` (0x00) | **PROVEN (119/119 Parsed, 0 Rejected)** |

---

## 5. Physical Stream Ground Truth Protocol

The physical indicator stream received over MA112 virtual COM port `COM3` @ `2400` baud 8N1 with DTR/RTS asserted follows the fixed 9-byte packet structure:

```text
Byte 0: 0x5B ('[')  -- Frame Start Header
Bytes 1-7: 7 ASCII numeric characters ('0'-'9') -- Payload (e.g. "0000300" = 300 kg)
Byte 8: 0x00 ('\0') -- Packet Null Terminator
```

### Decoded Weight Trajectory in Captured Hardware Stream:
- Frames 1–20: `0000300` $\rightarrow$ **300.0 kg**
- Frames 21–26: `0000350` $\rightarrow$ **350.0 kg**
- Frames 27–46: `0000250` $\rightarrow$ **250.0 kg**
- Frames 47–60: `0000200` $\rightarrow$ **200.0 kg**
- Frames 61–95: `0000250` $\rightarrow$ **250.0 kg**
- Frames 96–105: `0000300` $\rightarrow$ **300.0 kg**
- Frames 106–112: `0000350` $\rightarrow$ **350.0 kg**
- Frames 113–119: `0000400` $\rightarrow$ **400.0 kg**

---

## 6. Anti-Fabrication & Truthfulness Protocol
1. **No Simulated Assumptions:** Real hardware behavior must never be guessed or simulated when physical hardware testing is available.
2. **Immutable Binary Capture:** The `.bin` file generated during Phase 1 (`SerialCapture_2026-08-29_1254.bin`) is the sole ground truth. Parser unit tests are written against captured binary fixtures.
3. **End-to-End Value Fidelity:** The numeric weight value displayed on the scale display must match the raw hex, the parsed reading, the stability evaluator, and the WPF HUD with zero rounding or locale divergence:
   $$\text{Physical Wire (300)} \rightarrow \text{Extractor (0000300)} \rightarrow \text{Parser (300.0)} \rightarrow \text{Reading (300.0)} \rightarrow \text{HUD (300 kg)}$$
