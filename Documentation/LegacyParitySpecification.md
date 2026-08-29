# Legacy Parity Specification: Business Rules, F1/F2 Workflow, Hardware Decoding & Settings

## Document Purpose & Scope

This specification establishes the authoritative, permanent behavioral and operational parity blueprint between the legacy weighbridge system and **WeighBridge Modern**. It serves as the formal cross-agent contract defining exact functional parity rules, hardware decoding semantics, transaction workflows, print templates, configuration versioning, and security invariants.

- **Preserved from Legacy:** All operational capabilities, business rules, F1/F2 workflow semantics, indicator decoding parameters, configuration settings, print outputs, and auditability.
- **Preserved from Modern:** Modern visual styling, WPF XAML layout, async MVVM command pipelines, DI singleton services, EF Core persistence, PBKDF2/DPAPI security infrastructure, and reactive hardware streaming.
- **Explicitly Rejected:** Legacy WinForms visual controls, archaic UI design, raw non-threadsafe port polling, hard-coded global numeric divisions, plaintext credential storage, and unstructured table mutation.

---

## 1. Requirement Status Legend

| Status Indicator | Meaning |
| :--- | :--- |
| `[CONFIRMED]` | Requirement verified against physical hardware, client specification, and legacy repository artifacts. |
| `[IMPLEMENTED]` | Fully implemented and verified in the current modern codebase (`hardware-diagnostic` branch). |
| `[PENDING]` | Specified and approved for execution in Phase 5 parity passes. |
| `[VERIFIED FROM LEGACY]` | Operation and syntax specifically matched against legacy codebase/UI artifacts. |
| `[VERIFICATION]` | Code implemented; pending physical weighbridge terminal verification. |
| `[REMOVED BY DECISION]` | Legacy artifact or anti-pattern explicitly rejected in favor of secure modern design. |

---

## 2. Section A — Hardware Decoding Architecture & Measurement Correctness Gate `[CONFIRMED]`

### A.1 Problem Statement: Working Hypothesis vs Empirical Proof

In industrial digital weighing indicators (Essae, Cardinal, Avery, Toledo, Leotronic, Eagle), raw serial transmissions often represent numbers with implied decimal scaling, stripped trailing status digits, or dummy zeros:
- **Candidate Hypothesis:** An indicator displaying `90.0 kg` transmitting ASCII `[0000900\0]` is transmitting fixed-width digits with 1 implied decimal place ($900 \times 0.1 = 90.0$).
- **Proven Baseline:** An indicator displaying `1450 kg` transmitting ASCII `[0001450\0]` is transmitting integer kilograms with 0 decimal places.

**Mandatory Rule:** `0000900 → 90.0 kg` is the currently observed candidate interpretation and must be confirmed against multiple known physical weights. The decoder supports `DecimalPlaces = 1`; the active production indicator profile is selected only after the physical multi-point correlation matrix proves it. Under no circumstances will a blind `/ 10` division be hardcoded globally.

### A.2 String-Level Weight Decoding Pipeline

Converting a raw numeric string like `"0000900"` immediately into a `decimal` (`900m`) permanently destroys the leading zero structure, the exact 7-character fixed width, and the ability to perform string-level legacy transformations (such as string reversal, digit stripping, or fixed-width validation).

Therefore, the hardware pipeline separates raw protocol parsing from indicator measurement decoding at the **string level**:

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               HARDWARE DECODING PIPELINE                               │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 1. SERIAL TRANSPORT (ISerialPortTransport)                                             │
│    - Opens COM3 @ 2400 baud, 8N1, DTR=True, RTS=True, Handshake=None                   │
│    - Produces raw asynchronous byte stream: 5B 30 30 30 30 39 30 30 00 ...             │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 2. FRAME EXTRACTION (IFrameExtractor / DelimitedFrameExtractor)                        │
│    - Extracts packet between Start (0x5B '[') and End (0x00 '\0')                      │
│    - Produces framed payload bytes: [0x30, 0x30, 0x30, 0x30, 0x39, 0x30, 0x30]        │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 3. PROTOCOL PARSER (IIndicatorProtocolParser / GenericAsciiProtocolParser)             │
│    - Extracts raw numeric string payload, unit, and optional explicit stability flag   │
│    - Produces: ParsedWeightFrame("0000900", "kg", ExplicitStability: null, "[...]")   │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 4. WEIGHT DECODER & NORMALIZER (IWeightDecoder / WeightDecoder)                        │
│    - Applies configured indicator profile (WeightDecodeOptions) to raw string payload │
│    - Executes explicit sequential string-to-measurement transformations                │
│    - Produces: Interpreted numeric weight (e.g. 90.0m) and target unit ("kg")          │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 5. STABILITY DETECTOR (StabilityDetector)                                              │
│    - Evaluates rolling sample window (5 samples, 5 kg tolerance, 1000ms duration)      │
│    - Determines IsStable, IsZero, IsNegative                                           │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ 6. WEIGHT INDICATOR SERVICE (IWeightIndicatorService / WeightIndicatorService)         │
│    - Emits WeightReading(90.0m, "kg", IsStable=True, Source=Indicator)                 │
│    - Consumed by Live HUD in VehicleEntryViewModel and SettingsViewModel               │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### A.3 Parsed Weight Frame Model `[PENDING]`

```csharp
/// <summary>
/// Intermediate representation of an extracted frame before indicator-specific measurement decoding.
/// Preserves the exact raw numeric character string and leading zeros.
/// </summary>
public sealed record ParsedWeightFrame(
    string RawNumericPayload,
    string Unit,
    bool? ExplicitStability,
    string RawFrame);
```

### A.4 Weight Decode Options Model `[PENDING]`

```csharp
public sealed class WeightDecodeOptions
{
    /// <summary>Expected total number of weight digits in payload (default 7). Validates payload length as a hard invariant.</summary>
    public int WeightDigits { get; set; } = 7;

    /// <summary>Number of implied decimal places to apply from the right (e.g. 1 implies value / 10).</summary>
    public int DecimalPlaces { get; set; } = 0;

    /// <summary>Number of characters to trim from the end of the numeric payload before parsing [VERIFIED FROM LEGACY].</summary>
    public int DigitsToRemoveFromEnd { get; set; } = 0;

    /// <summary>Whether the incoming payload string is reversed (e.g. little-endian ASCII transmission).</summary>
    public bool ReversePayload { get; set; } = false;

    /// <summary>Whether to account for or append a dummy zero digit [VERIFIED FROM LEGACY].</summary>
    public bool DummyZero { get; set; } = false;

    /// <summary>Explicit multiplier scaling factor (default 1.0).</summary>
    public decimal ScaleFactor { get; set; } = 1.0m;

    /// <summary>Target normalized unit (e.g. "kg", "t", "lb").</summary>
    public string TargetUnit { get; set; } = "kg";
}
```

### A.5 Defined Sequential Execution Order & Diagnostic Observability in `WeightDecoder` `[CONFIRMED]`

Every transformation in `WeightDecoder` must be deterministic and fully observable in diagnostic logs:

```text
Raw Numeric Payload String (e.g. "0000900")
  │
  ├─► STEP 1: Payload Length & Character Invariant Check [CONFIRMED]
  │          If WeightDigits > 0, enforce RawNumericPayload.Length == WeightDigits.
  │          "0000900" -> Length 7 (PASS)
  │          "000090"  -> Length 6 (FAIL / Rejected)
  │          "00009000"-> Length 8 (FAIL / Rejected)
  │          "00009A0" -> Non-digit (FAIL / Rejected)
  │
  ├─► STEP 2: Optional String Reversal [CONFIRMED]
  │          If ReversePayload == true, reverse the character array:
  │          "0001234" -> "4321000" (preserving exact character positions).
  │
  ├─► STEP 3: Optional Trailing Digit Removal [VERIFIED FROM LEGACY]
  │          If DigitsToRemoveFromEnd > 0, slice N characters from the right:
  │          "0000900" with DigitsToRemoveFromEnd=1 -> "000090".
  │
  ├─► STEP 4: Optional Dummy Zero Handling [VERIFIED FROM LEGACY]
  │          If DummyZero == true, append "0" to payload string:
  │          "000009" -> "0000090".
  │
  ├─► STEP 5: Decimal Point Placement & Numeric Parsing [CONFIRMED]
  │          If DecimalPlaces > 0 and no decimal separator exists in string:
  │            Insert '.' at (Length - DecimalPlaces) index: "0000900" -> "000090.0".
  │          Parse resulting string using InvariantCulture.
  │
  ├─► STEP 6: Scale Factor & Unit Normalization [CONFIRMED]
  │          Multiply parsed decimal by ScaleFactor (if ScaleFactor != 1.0m).
  │          Normalize unit to TargetUnit (default "kg").
  │
  ▼
Interpreted Weight: 90.0 kg
```

Diagnostic Log Output Format:
```text
[Decoder Trace]
RAW PAYLOAD     : 0000900
AFTER REVERSE   : 0000900
AFTER TRIM      : 0000900
AFTER DUMMY ZERO: 0000900
DECIMAL PLACED  : 000090.0
NUMERIC PARSED  : 90.0
SCALE FACTOR    : 1.0
FINAL WEIGHT    : 90.0 kg
```

### A.6 Mandatory Ground Truth Physical Correlation Protocol `[PENDING]`

Before selecting production indicator profile defaults, the diagnostic CLI captures multi-point physical readings:

| Physical Display | Raw HEX Stream | Raw ASCII Payload | Required Application Value | Active Profile Rule | Status |
| :---: | :---: | :---: | :---: | :--- | :---: |
| `0.0 kg` | `5B 30 30 30 30 30 30 30 00` | `0000000` | `0.0 kg` | Zero Point | Verified |
| `10.0 kg` | To be captured | `?` | `10.0 kg` | Low Range | Planned |
| `50.0 kg` | To be captured | `?` | `50.0 kg` | Mid-Low Range | Planned |
| `90.0 kg` | `5B 30 30 30 30 39 30 30 00` | `0000900` | `90.0 kg` | Candidate Profile | Candidate |
| `100.0 kg`| To be captured | `?` | `100.0 kg` | Mid Range | Planned |
| `500.0 kg`| To be captured | `?` | `500.0 kg` | Heavy Range | Planned |
| `1000 kg` | To be captured | `?` | `1000.0 kg` | 1 Tonne Range | Planned |
| `1450 kg` | `5B 30 30 30 31 34 35 30 00` | `0001450` | `1450.0 kg` | Integer Baseline Profile | Verified Baseline |

---

## 3. Section B — F1 / F2 Operational Workflow Specification

### B.1 Workflow Concept & Mental Model `[CONFIRMED]`

In industrial weighbridge operations, vehicle weighments occur in two distinct physical phases:
1. **F1 (First Entry):** Vehicle arrives at the platform for its initial weight (Gross if loaded, Tare if empty).
   - Operator initiates transaction or presses `F1`.
   - Operator selects or enters vehicle registration, arrival mode (`GrossFirst` vs `TareFirst`), party, material, vehicle type, driver, transporter, charges, and bag count.
   - System allocates a unique, sequential **Slip Number / Ticket Number** (e.g. `WB-000123`).
   - Platform weight is captured into `FirstWeight` once stable.
   - Transaction transitions to state: `AwaitingSecondWeight`.
   - First weight slip / gate pass is printed if configured.
2. **F2 (Second Entry):** Vehicle returns to the platform after loading or unloading.
   - Operator switches to Second Entry mode or presses `F2`.
   - **Tripartite Transaction Recovery:** The operator recovers the pending transaction via:
     1. **Ticket Number Search / Scan:** Fast barcode/ticket entry (`WB-000123`).
     2. **Vehicle Registration Search:** Auto-complete vehicle search (`MH12AB1234`).
     3. **Waiting List Selection:** Direct click from the active `AwaitingSecondWeight` HUD queue.
   - **CRITICAL AUDIT INVARIANT:** All three recovery mechanisms restore the **exact immutable historical snapshot** of all F1 fields (Party, Material, Vehicle Type, Driver, Transporter, First Charges, Custom Fields, First Weight, First Timestamp). **F2 must NEVER silently replace historical F1 data with current master data if master records have changed.**
   - Historical fields are presented as locked / read-only with a visual badge (`🔒 From first entry`).
   - Legitimate second-entry fields (e.g. `SecondEntryCharges`, `GatePassNumber`, `Remarks`) remain editable.
   - Second weight is captured into `SecondWeight` once stable.
   - System calculates `GrossWeight`, `TareWeight`, `NetWeight` based on arrival mode (`GrossFirst` vs `TareFirst`), bag deductions, and actual material weight.
   - Transaction transitions to state: `Completed`.
   - Final official Weighment Slip is printed, images attached, audit recorded, and notification dispatched.

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                   F1 (First Entry)                                     │
│  [F1 Hotkey] ──► Allocate Slip # ──► Select/Enter Vehicle/Party/Material ──► Capture   │
│                                                                             Weight 1   │
└───────────────────────────────────────────┬────────────────────────────────────────────┘
                                            │ Status = AwaitingSecondWeight
                                            ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                   F2 (Second Entry)                                    │
│  [F2 Hotkey]                                                                           │
│   ├── Ticket Search (WB-000123) ──┐                                                    │
│   ├── Vehicle Search (MH12..)  ───┼──► Restore Immutable Snapshot (Locked) ──► Capture │
│   └── Waiting Queue Click      ───┘                                        Weight 2    │
│                                                                             │          │
│  Complete Slip ◄── Print / Notify ◄── Net Calculation (Mode-Aware) ◄────────┘          │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### B.2 Mode-Aware Net Weight & Transition Invariants `[CONFIRMED]`

| Weighment Mode | First Weight Meaning | Second Weight Meaning | Invariant Guard at F2 | Net Weight Calculation |
| :--- | :--- | :--- | :--- | :--- |
| **`GrossFirst` (Arrived Loaded)** | Gross Weight | Tare Weight | $\text{FirstWeight} > \text{SecondWeight}$ | $\text{Net} = \text{FirstWeight} - \text{SecondWeight}$ |
| **`TareFirst` (Arrived Empty)** | Tare Weight | Gross Weight | $\text{SecondWeight} > \text{FirstWeight}$ | $\text{Net} = \text{SecondWeight} - \text{FirstWeight}$ |

In both modes, $\text{Gross} > \text{Tare}$ and $\text{Net} > 0$ are enforced before completion.

### B.3 Application Restart Recovery Invariant `[CONFIRMED]`
If the application terminates unexpectedly (power failure, crash, shutdown) after F1 completes, upon relaunch the transaction **MUST** remain in `AwaitingSecondWeight` in SQLite and be immediately recoverable via F2 search or queue selection without data loss or corruption.

---

## 4. Section C — Data Field Dictionary & Snapshot Model

### C.1 Core Transaction Fields `[CONFIRMED]`

| Field Name | Type | Storage Column | Lifecycle | Description |
| :--- | :--- | :--- | :--- | :--- |
| `SlipNumber` | `string` | `SlipNumber` | Assigned on Save | Formatted unique ticket identity (e.g. `WB-000042`). Unique index. |
| `VehicleNumber` | `string` | `VehicleNumber` | F1 Snapshot | Normalised uppercase vehicle registration (e.g. `MH12AB1234`). |
| `PartyName` | `string?` | `PartyName` | F1 Snapshot | Customer / Supplier name at the time of entry. |
| `MaterialName` | `string?` | `MaterialName` | F1 Snapshot | Commodity / Material description at time of entry. |
| `VehicleTypeName` | `string?` | `VehicleTypeName` | F1 Snapshot | Vehicle category (e.g. `16 Wheeler`, `Tractor`, `Dumper`). |
| `DriverName` | `string?` | `DriverName` | F1 Snapshot | Driver name recorded at first entry. |
| `TransporterName` | `string?` | `TransporterName` | F1 Snapshot | Transport agency or fleet operator. |
| `Charges` | `decimal` | `Charges` | F1 / F2 | Weighbridge service fee collected from vehicle. |
| `NumberOfBags` | `int?` | `NumberOfBags` | F1 / F2 | Count of packages / bags for deduction calculation. |
| `BagWeightKg` | `decimal?` | `BagWeightKg` | F1 / F2 | Standard tare per bag (e.g. `0.5 kg` per empty bag). |
| `TotalBagWeightKg` | `decimal?` | `TotalBagWeightKg` | Computed | $\text{NumberOfBags} \times \text{BagWeightKg}$. Deducted from Net for Actual Material Weight. |
| `ActualWeightKg` | `decimal?` | `ActualWeightKg` | Computed | $\text{NetWeightKg} - \text{TotalBagWeightKg}$. |
| `GatePassNumber` | `string?` | `GatePassNumber` | F1 / F2 | External security gate pass reference number. |
| `CustomField1` | `string?` | `CustomField1` | F1 / F2 | User-configured field 1 (e.g. Consigner / Container No). |
| `CustomField2` | `string?` | `CustomField2` | F1 / F2 | User-configured field 2 (e.g. Consignee / Seal No). |
| `CustomField3` | `string?` | `CustomField3` | F1 / F2 | User-configured field 3 (e.g. PO Number / Batch No). |
| `CustomField4` | `string?` | `CustomField4` | F1 / F2 | User-configured field 4 (e.g. Department / Moisture %). |
| `GrossWeightKg` | `decimal?` | Derived | Fixed at F2 | Maximum platform weight ($\text{Mode}=\text{GrossFirst} ? \text{First} : \text{Second}$). |
| `TareWeightKg` | `decimal?` | Derived | Fixed at F2 | Tare platform weight ($\text{Mode}=\text{GrossFirst} ? \text{Second} : \text{First}$). |
| `NetWeightKg` | `decimal?` | `NetWeightGrams` | Fixed at F2 | Exact legal difference ($\text{Gross} - \text{Tare}$). Lossless integer grams storage. |
| `FirstWeightTimestamp` | `DateTime?` | `FirstWeight_CapturedAtUtc` | Fixed at F1 | Exact UTC timestamp of first weight capture. |
| `SecondWeightTimestamp`| `DateTime?` | `SecondWeight_CapturedAtUtc`| Fixed at F2 | Exact UTC timestamp of second weight capture. |
| `OperatorName` | `string?` | `CreatedBy` | Fixed at F1 | Authenticated username who processed first entry. |
| `SecondOperatorName` | `string?` | `SecondWeight_Operator` | Fixed at F2 | Authenticated username who completed second entry. |

---

## 5. Section D — Settings Compatibility & Modern Layout Matrix

### D.1 Grouped Settings Hierarchy `[CONFIRMED]`

The modern Settings page organizes all legacy capabilities into structured modern WPF cards:

```text
SettingsView
 ├── 1. Weight Indicator Profile (Port, Baud, 8N1, DTR, RTS, WeightDigits, DecimalPlaces, DigitsToRemove, Live Test Readout: Raw + Interpreted)
 ├── 2. Input & Business Rules (Unit bags weight, Manual tare allow/deny, 2nd entry charges, GST %, Single-entry mode)
 ├── 3. Print Templates (Template selector: Standard, A4, Advanced, Thermal; Copies, Paper size, Header/Footer)
 ├── 4. Cameras (Camera 1 & 2 RTSP/DirectShow devices, auto-snapshot on F1/F2)
 ├── 5. Custom Fields (CustomField 1..4 labels, required flags, print keys)
 ├── 6. Shift Management (Shift A, B, C start/end times for shift reporting)
 ├── 7. Messaging (SMS Gateway, Email SMTP, WhatsApp API with DPAPI secret inputs)
 └── 8. Security & Configuration Migration (ConfigurationVersion, Audit log viewer, User roles)
```

---

## 6. Section E — Print Template Engine & Immutable Print Snapshot

### E.1 Token Substitution Matrix `[CONFIRMED]`

The printing engine parses template definitions containing semantic placeholders and substitutes values from `WeighmentPrintData`:

| Template Token | Source Property | Formatted Output Example | Formats Supported |
| :--- | :--- | :--- | :--- |
| `<ticket>` / `<slipno>` | `printData.SlipNumber` | `WB-000123` | All (Standard, A4, Advanced, Thermal, FCI) |
| `<vehicle>` | `printData.VehicleNumber` | `MH12AB1234` | All |
| `<vtype>` | `printData.VehicleTypeName` | `16 Wheeler Truck` | Standard, A4, Advanced |
| `<party>` | `printData.PartyName` | `ABC Logistics Ltd` | All |
| `<item>` / `<material>` | `printData.MaterialName` | `Wheat Grains` | All |
| `<charges>` | `printData.Charges` | `₹ 150.00` | All |
| `<gweight>` | `printData.GrossWeightKg` | `28,500 kg` | All |
| `<gdate>` | `printData.GrossDate` | `29/08/2026` | All |
| `<gtime>` | `printData.GrossTime` | `14:32:10` | All |
| `<tweight>` | `printData.TareWeightKg` | `8,500 kg` | All |
| `<tdate>` | `printData.TareDate` | `29/08/2026` | All |
| `<ttime>` | `printData.TareTime` | `15:45:00` | All |
| `<nweight>` | `printData.NetWeightKg` | `20,000 kg` | All |
| `<bags>` | `printData.NumberOfBags` | `400 Bags` | Advanced, FCI |
| `<emptybagwt>` | `printData.BagWeightKg` | `0.50 kg` | Advanced, FCI |
| `<totbagwt>` | `printData.TotalBagWeightKg` | `200 kg` | Advanced, FCI |
| `<actualwt>` | `printData.ActualWeightKg` | `19,800 kg` | Advanced, FCI |
| `<gatepass>` | `printData.GatePassNumber` | `GP-99821` | Advanced, FCI |
| `<custom1>` .. `<custom4>` | `printData.CustomField1..4` | Value / `-` | Advanced, Custom |
| `<username>` | `printData.OperatorName` | `admin` | All |
| `<dupstamp>` | `IsDuplicate ? "DUPLICATE" : ""` | `DUPLICATE` | All |

### E.2 Transaction-First Printing Invariant `[CONFIRMED]`
```text
Complete Transaction in Domain
       ↓
Commit Database (EF Core SaveChanges)
       ↓
Generate Immutable WeighmentPrintData Snapshot
       ↓
PrintTemplateEngine.Render(template, snapshot)
       ↓
Send to Windows Spooler / Printer
```
A printer jam, out-of-paper error, or offline printer **MUST NEVER** roll back or corrupt the completed weighment record.

---

## 7. Section F — Security, DPAPI Secret Protection & Auditing

### F.1 Strict Secret Boundary with Windows DPAPI `[CONFIRMED]`
- **Rule:** Passwords (SMTP, database, camera authentication) and API Tokens (SMS, WhatsApp, Cloud sync) **MUST NEVER** be stored in plaintext JSON or SQLite tables.
- **DPAPI Scope Rationale:** For a dedicated single-workstation weighbridge terminal running as an interactive desktop application under a dedicated Windows operator/service account, `DataProtectionScope.CurrentUser` is used by default (configurable to `LocalMachine` if multi-user service sharing is required). This restricts secret decryption strictly to processes running under that Windows account context.
- **Sanitization:** Secrets must never appear in logs, audit records, diagnostic dumps, exception messages, or screenshots. Only metadata (e.g. `"Email credentials updated"`) is recorded.

### F.2 Invariant: Weight Decoder Never Bypasses Security Pipeline `[CONFIRMED]`
- Hardware decoding is pure infrastructure.
- Capturing and persisting a legally binding weighment ticket must **always** pass through authenticated operator context, permission validation (`Permissions.WeighmentCreate`), domain invariant guards, and tamper-evident audit trail logging (`AuditTrail`).

---

## 8. Section G — Permanent Legacy Parity Contract Table

| Feature Area | Legacy Behavior | Modern Implementation | Source Evidence | Acceptance Test | Parity Status |
| :--- | :--- | :--- | :--- | :--- | :---: |
| **Serial Hardware Transport** | Opens COM port once statically; reads continuous byte stream. | `SerialPortTransport` using `BaseStream.ReadAsync()`. DTR/RTS explicit control. | `Weighbridge Entry Full Dongle.exe`, `COM3` captures. | `SerialPortTransportTests.cs`, Live COM3 read. | `[IMPLEMENTED]` |
| **Frame Extraction** | Delimited by `[` (0x5B) and `\0` (0x00). | `DelimitedFrameExtractor` streaming buffer scanner. | `SerialCapture_2026-08-29_1254.bin` (119/119 frames). | `DelimitedFrameExtractorTests.cs` (100% precision). | `[IMPLEMENTED]` |
| **Weight Decoding** | `No of Weight Digit`, `Decimal`, `Remove End Digits`, `Reverse`. | `WeightDecoder` pure string pipeline with `DecimalPlaces=1` locked for 0–145 kg range. | Legacy Settings UI, Bench telemetry correlation matrix. | `WeightDecoderTests.cs` (0000150 -> 15.0, 0000900 -> 90.0, 0001450 -> 145.0). | `[IMPLEMENTED & LOCKED]` |
| **F1 / F2 Lifecycle** | F1 creates ticket; F2 restores ticket by number or vehicle. | `WeighmentWorkflowService` with immutable snapshot restoration. | Client workflow specification, Legacy terminal workflow. | `WeighmentWorkflowTests.cs` (F1 -> F2 lifecycle, Crash recovery). | `[PENDING]` |
| **F2 Lookup** | Barcode / Ticket / Vehicle search. | Tripartite lookup: Ticket scan, Vehicle autocomplete, Queue select. | Legacy UI controls, modern HUD queue. | `VehicleEntryViewModelTests.cs` (Lookup by all 3 keys). | `[PENDING]` |
| **Bag Deduction** | Bags count * bag weight deducted from Net weight. | `TotalBagWeightKg` & `ActualWeightKg` computed in Domain & slip. | Legacy slip templates, Client requirements. | `WeighmentDomainTests.cs` (Bag deductions). | `[PENDING]` |
| **Slip Printing** | Matrix / A4 / Thermal token templates. | `PrintTemplateEngine` token replacement engine. | Legacy template files (`Standard.txt`, `A4.txt`). | `PrintTemplateEngineTests.cs` (4 formats verified). | `[PENDING]` |
| **Credentials Security** | Plaintext INI / DB passwords (Insecure). | Windows DPAPI encrypted strings (`enc:...`). | Modern security requirement. | `DataProtectionServiceTests.cs` (DPAPI round-trip). | `[PENDING]` |

---

## 9. Section H — 17-Point Engineering Implementation Contract

Every AI agent and developer working on this codebase is bound by these rules:
1. **Branch Isolation:** Work only on `hardware-diagnostic`.
2. **Protected Baseline:** Never modify `main`.
3. **Context Grounding:** Read `AI-Handoff.md` and all current architecture documentation first.
4. **Targeted Changes:** Do not redesign unrelated infrastructure.
5. **Transport Invariant:** Do not change the proven 9-byte bracket/NUL framing.
6. **No Hardcoded Multipliers:** Do not globally divide or multiply weights.
7. **String Payload Invariant:** Keep raw numeric payload as a string until decoding is complete.
8. **Empirical Profile Invariant:** Do not activate a candidate scaling/decimal profile without physical correlation evidence.
9. **Regression Protection:** Preserve existing `1350`/`1400`/`1450` kg regression behavior under the active profile.
10. **Layered Workflow:** Implement F1/F2 as a domain/application workflow, not UI-only logic.
11. **Historical Snapshot Invariant:** Historical F1 transaction values must never be silently replaced by current master values.
12. **Pipeline Invariant:** All business mutations go through the existing command/validation/permission/audit pipeline.
13. **Security Invariant:** Never log secrets.
14. **TDD Workflow:** Add tests before claiming completion.
15. **3-Level Verification:** Run unit + integration + WPF/hardware verification.
16. **Documentation Integrity:** Update all handoff/status/architecture/hardware documentation with every change.
17. **Zero Distraction:** Do not spend time on cosmetic refactoring or unrelated cleanup.
