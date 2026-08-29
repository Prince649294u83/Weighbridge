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

## 5. Physical Serial Configuration & Ground Truth Protocol

### 5.1 Verified Physical Serial Interface
The physical connection via Megawin MA112 USB-to-UART bridge operates strictly under:
```text
Port Name     : COM3 (Configurable per installation, verified for this bench)
Baud Rate     : 2400 baud
Data Bits     : 8
Parity        : None (8N1)
Stop Bits     : One
DTR Enable    : True (Hardware asserted)
RTS Enable    : True (Hardware asserted)
Handshake     : None
Framing       : Fixed 9-byte packet [0x5B, 7 ASCII digits, 0x00]
```

### 5.2 Status: Physical Measurement Correlation Recorded (Ratio Analysis Complete)

```text
Decoder Engine Capabilities (Pure string transformations)  --> COMPLETED & TESTED (Frozen)
Empirical Ratio Analysis (Physical Display vs Raw Payload) --> PROVEN 10.0x ACROSS ALL POINTS
Active Production Indicator Profile (Configuration)        --> READY FOR ACTIVATION REVIEW
Phase 2 (F1/F2 Operational Workflow)                       --> GATED ON ACTIVATION REVIEW
```

### 5.3 Multi-Point Physical Correlation Evidence Table

Tracked permanently in `tests/Fixtures/Hardware/indicator-com3-2400/correlation.json`:

| Physical Display (Authority) | Raw HEX Stream | Raw ASCII Payload | Current App (`DecimalPlaces=0`) | Measured Ratio | Analysis & Notes |
| :---: | :---: | :---: | :---: | :---: | :--- |
| `0.0 kg` | `5B 30 30 30 30 30 30 30 00` | `0000000` | `0.0 kg` | `1.0x` | Verified Zero Point |
| `5.0 kg` | `5B 30 30 30 30 30 35 30 00` | `0000050` | `50.0 kg` | `10.0x` | Live hardware telemetry capture |
| `10.0 kg` | `5B 30 30 30 30 31 30 30 00` | `0000100` | `100.0 kg` | `10.0x` | Live hardware telemetry capture |
| `15.0 kg` | `5B 30 30 30 30 31 35 30 00` | `0000150` | `150.0 kg` | `10.0x` | **Direct Bench Benchmark**: Physical readout is 15 kg, raw stream is `0000150` |
| `20.0 kg` | `5B 30 30 30 30 32 30 30 00` | `0000200` | `200.0 kg` | `10.0x` | Live hardware telemetry capture |
| `90.0 kg` | `5B 30 30 30 30 39 30 30 00` | `0000900` | `900.0 kg` | `10.0x` | **Direct Bench Benchmark**: Physical readout is 90 kg, raw stream is `0000900` |
| `95.0 kg` | `5B 30 30 30 30 39 35 30 00` | `0000950` | `950.0 kg` | `10.0x` | Live hardware telemetry capture |
| `100.0 kg` | `5B 30 30 30 31 30 30 30 00` | `0001000` | `1000.0 kg` | `10.0x` | Live hardware telemetry capture |
| `105.0 kg` | `5B 30 30 30 31 30 35 30 00` | `0001050` | `1050.0 kg` | `10.0x` | Live hardware telemetry capture |
| `110.0 kg` | `5B 30 30 30 31 31 30 30 00` | `0001100` | `1100.0 kg` | `10.0x` | Live hardware telemetry capture |
| `135.0 kg` | `5B 30 30 30 31 33 35 30 00` | `0001350` | `1350.0 kg` | `10.0x` | Historical capture clarified: load was 135.0 kg (2 persons) |
| `140.0 kg` | `5B 30 30 30 31 34 30 30 00` | `0001400` | `1400.0 kg` | `10.0x` | Historical capture clarified: load was 140.0 kg |
| `145.0 kg` | `5B 30 30 30 31 34 35 30 00` | `0001450` | `1450.0 kg` | `10.0x` | Historical capture clarified: load was 145.0 kg (2 persons) |

### 5.4 Root Cause Synthesis: Implied Decimal Position in Serial Protocol
The physical digital indicator operates with a fixed single implied decimal digit transmitted in the 7-digit ASCII serial frame:
$$\text{Raw ASCII Payload} = (\text{Physical Value in kg} \times 10).\text{PadLeft}(7, \mathtt{'0'})$$
- The previous assumption that `0001450` was 1,450 kg was a measurement interpretation artifact: the physical platform load was actually **145.0 kg** (two people standing on the platform), which sent `0001450`.
- Under `WeightDecodeOptions { DecimalPlaces = 1 }`, every single measured point ($0, 5, 10, 15, 20, 90, 95, 100, 105, 110, 135, 140, 145\text{ kg}$) resolves with 100% mathematical precision to the physical scale display.

---

## 6. Architecture: Capabilities vs. Active Profile
1. **Decoder Capabilities:** `WeightDecoder` pure string transformation engine supports `WeightDigits`, `DecimalPlaces`, `DigitsToRemoveFromEnd`, `ReversePayload`, `DummyZero`, and `ScaleFactor`.
2. **Active Profile Isolation:** Profile selection is governed by configuration (`WeightIndicator.Decoding.DecimalPlaces = 1`), leaving the core engine generic and unpolluted by hardcoded division.
3. **No Global `/ 10` Arithmetic:** Global hardcoding is strictly avoided; `WeightDecoder` interprets payloads via the configured `WeightDecodeOptions`.
4. **Permanent Fixtures:** Ground truth is maintained in `tests/Fixtures/Hardware/` for reproducible offline verification.

