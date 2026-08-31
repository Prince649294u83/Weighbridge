# F1 / F2 Operational Workflow & Legacy Behavioral Specification

> **Status:** Authoritative Architectural & Behavioral Specification (Locked, Approved & Sealed)  
> **Target Branch:** `hardware-diagnostic` (Ready for Execution)  
> **Protected Branch:** `main` (Untouched @ `77d702c`)  
> **Hardware Baseline:** Phase 1 Measurement Profile Locked (`DecimalPlaces = 1`, 660 tests passed, `f060d31`)
> **Legacy Reference Path:** `E:\Projects\Weighbridge Entry Dongle 2025\Weighbridge Entry Dongle`

---

## 1. Legacy Repository Evidence Classification & Ground Rules

### 1.1 Ground Truth vs Inferences vs Modern Product Decisions

The legacy codebase directory at `E:\Projects\Weighbridge Entry Dongle 2025\Weighbridge Entry Dongle` contains compiled application binaries (`Terminal.exe`, `Weighbridge Entry Full Dongle.exe`), Access database `WBS.accdb`, runtime operational log `Logs.txt`, print templates (`Print_Ticket*.txt`), SMS format template (`SMS Format.txt`), and supporting libraries. It does **not** expose original C#/VB source code for direct line-by-line inspection.

Therefore, this specification strictly classifies all behaviors into four evidential categories:

1. **`[LEGACY VERIFIED]`**: Proven by concrete legacy repo artifacts (e.g., `Logs.txt`, `Print_Ticket*.txt`, `SMS Format.txt`, database schema).
2. **`[LEGACY INFERRED]`**: Deduced from UI screenshots, dialog messages, or compiled binary dependencies.
3. **`[MODERN PRODUCT REQUIREMENT]`**: Explicit modern architectural or business decision chosen where legacy source code is absent or was archaic.
4. **`[REMOVED BY DECISION]`**: Legacy capabilities explicitly removed from Modern WeighBridge (e.g., RFID / UHF Reader).

---

### 1.2 Legacy Evidence Matrix

| Area / Feature | Legacy Artifact / Source | Evidence Type | Status / Classification | Modern Implementation Rule |
| :--- | :--- | :--- | :--- | :--- |
| **Startup Sequence** | `Logs.txt` (`DB Updated` $\rightarrow$ `DB Settings Loaded` $\rightarrow$ `Opening Port` $\rightarrow$ `Opening Send Port` $\rightarrow$ `AutoMaster Loading`) | Operational Log | **`[LEGACY VERIFIED]`** | Startup ensures DB, configuration, hardware transport, and master caches are ready before operator workflows begin. |
| **Core Ticket Fields** | `Print_Ticket.txt` (`<ticket>`, `<party>`, `<vehicle>`, `<vtype>`, `<item>`, `<charges>`, `<gweight>`, `<tweight>`, `<nweight>`, `<username>`) | Print Template | **`[LEGACY VERIFIED]`** | Primary transaction aggregate permanently retains Ticket, Vehicle, Party, Material, Vehicle Type, Charges, Gross, Tare, Net, Operator. |
| **Advanced Packaging & Deductions** | `Print_Ticket_Advanced.txt` (`<bags>`, `<emptybagwt>`, `<gatepass>`, `<acualwt>`) | Print Template | **`[LEGACY VERIFIED]`** | Second entry captures packaging bags, empty bag weight, gate pass, and computes actual material weight ($\text{Net} - \text{Bags} \times \text{BagWeight}$). |
| **FCI Food Grain Fields** | `Print_Ticket_FCI.txt` (`<ticket>`, `<party>` [Consignee], `<vtype>` [GR/GI], `<item>` [Food Grain], `<field1>` [Consigner], `<field2>` [Bags], `<field3>` [Empty Bag], `<field4>` [Material Wt]) | Print Template | **`[LEGACY VERIFIED]`** | Generic custom fields (`CustomField1..4`) support specialized workflow templates (e.g. FCI Consigner, GR/GI, Moisture). |
| **Thermal Slip Output** | `Print_Ticket_Thermal.txt` (Compact format + `<Cut>`) | Print Template | **`[LEGACY VERIFIED]`** | Compact receipt printer profile supported without altering core transaction invariants. |
| **SMS Transaction Fields** | `SMS Format.txt` (`ticketno`, `vehicleno`, `party`, `item`, `charges`, `vehicaltype`, `Gwt`, `Twt`, `Nettwt`) | SMS Template | **`[LEGACY VERIFIED]`** | Canonical summary data model provides lossless payload for future SMS dispatch. |
| **RFID / UHF Reader** | `UHFReader18CSharp.dll` | Binary Assembly | **`[REMOVED BY DECISION]`** | **PERMANENTLY EXCLUDED.** Modern WeighBridge will NOT include RFID hardware or tag polling. |
| **Exact F1/F2 Key Handlers** | Source code absent in legacy binaries | None (Binary) | **`[MODERN PRODUCT REQUIREMENT]`** | Explicit WPF Command / KeyBinding pipeline for `F1`, `F2`, `F3`, `F5`, `Esc` with focus-resilience across TextBoxes/ComboBoxes. |
| **Exact Ticket Number Allocator** | Source code absent in legacy binaries | None (Binary) | **`[MODERN PRODUCT REQUIREMENT]`** | Persisted database allocation via `IWeighmentService.CreateAsync` (`WB-XXXXXX`). Zero fake client-side numbers. |
| **Exact Field Locking Mechanics** | Source code absent in legacy binaries | None (Binary) | **`[MODERN PRODUCT REQUIREMENT]`** | Domain aggregate immutability: F1 fields strictly locked in `AwaitingSecondWeight`; Presentation snapshot renders locked controls with visual 🔒 indicators. |
| **Concurrency Token Mechanics** | Source code absent in legacy binaries | None (Binary) | **`[MODERN PRODUCT REQUIREMENT]`** | Optimistic concurrency via `Guid Version` token mapped with EF Core `IsConcurrencyToken()` and tested across independent SQLite `DbContext` instances. |

---

## 2. Authoritative Decisions & State Machine

In industrial weighbridge operations, vehicle transactions are processed in two physically separate operational stages:
1. **F1 (First Entry):** Vehicle arrives at the platform for initial weighing (Gross if arriving loaded, Tare if arriving empty). A pending ticket is created, assigned a database identity and formatted slip number (`WB-XXXXXX`), and committed to SQLite. Once the first weight is captured, all F1 historical data is **strictly locked and immutable**.
2. **F2 (Second Entry):** Vehicle returns after loading or tipping. The pending transaction is retrieved via ticket number, vehicle registration, or waiting queue. The **full `Weighment` aggregate is loaded** (bound by persistent `Weighment.Id` and `Version`), restoring the exact immutable historical F1 snapshot. The second weight is captured, Net weight is calculated according to the arrival mode and `NetWeightPolicy`, bag deductions are applied, and the transaction is legally completed.

---

### The 29 Authoritative Decisions

| # | Topic | Classification | Authoritative Decision |
| :---: | :--- | :---: | :--- |
| **1** | **Service Boundary** | `[MODERN PRODUCT REQUIREMENT]` | `IWeighmentService` is the **sole** business boundary. No parallel `IWeighmentWorkflowService` is created. ViewModels talk directly to `IWeighmentService`. |
| **2** | **F2 Scope** | `[MODERN PRODUCT REQUIREMENT]` | `FindPendingSecondEntryAsync` queries **strictly** for `Status == WeighmentStatus.AwaitingSecondWeight`. `Created`, `Completed`, and `Cancelled` are excluded. |
| **3** | **Interrupted F1** | `[MODERN PRODUCT REQUIREMENT]` | Interrupted F1 entries remain in `Created`. They can be resumed in F1 to capture first weight or cancelled. No arbitrary timeout or silent deletion. |
| **4** | **F1 Lock Timing** | `[MODERN PRODUCT REQUIREMENT]` | F1 historical details become strictly immutable immediately after first-weight capture (`AwaitingSecondWeight`). `UpdateDetails(...)` throws if not `Created`. |
| **5** | **Historical F1 Snapshot** | `[MODERN PRODUCT REQUIREMENT]` | Historical fields (`Vehicle`, `Party`, `Material`, `Driver`, `Transporter`, `FirstWeight`, `F1 Operator`) are restored from the `Weighment` aggregate entity; never rehydrated from current masters. |
| **6** | **F1 vs F2 Custom Fields** | `[LEGACY VERIFIED]` | `CustomField1` & `CustomField2` are captured at F1 and locked in F2. `CustomField3` & `CustomField4`, `SecondCharges`, `GatePassNumber`, `NumberOfBags`, `BagWeightKg`, and `Remarks` are editable in F2. |
| **7** | **F2 Entity Identity** | `[MODERN PRODUCT REQUIREMENT]` | F2 operations bind strictly to `ActiveWeighmentId` and `ActiveVersion`. Completion operates on these tokens, never on text from search inputs or textboxes. |
| **8** | **Rapid Double-F5** | `[MODERN PRODUCT REQUIREMENT]` | Rapid double-tap of `F5` produces exactly **one** database completion, one completion event, and one slip. The second invocation is safely ignored/rejected via `IsBusy` guard. |
| **9** | **Escape / Clear in F2** | `[MODERN PRODUCT REQUIREMENT]` | `Esc` clears only the active UI transaction context (`ActiveWeighmentId`, `ActiveVersion`, form fields) without modifying or cancelling the database row. |
| **10** | **Ticket Numbering** | `[MODERN PRODUCT REQUIREMENT]` | Preserves identity-derived `SlipNumbers` (`WB-XXXXXX`). Guarantees uniqueness and monotonic progression of committed records; no unproven claim of mathematical gaplessness across crashes/rollbacks. |
| **11** | **Concurrent F1 (Same Vehicle)** | `[MODERN PRODUCT REQUIREMENT]` | 100 concurrent F1 requests for the same vehicle result in **exactly 1** success and 99 clean rejections via the open-vehicle partial unique index. |
| **12** | **Concurrent F1 (Different Vehicles)** | `[MODERN PRODUCT REQUIREMENT]` | 100 concurrent F1 requests for different vehicles proceed independently and all succeed. |
| **13** | **Optimistic Concurrency** | `[MODERN PRODUCT REQUIREMENT]` | Application-managed `Guid Version` concurrency token regenerates on every material transition (`F1 Open`, `RecordFirstWeight`, `UpdateSecondEntryDetails`, `RecordSecondWeight`, `Cancel`). Enforced at `SaveChanges` boundary via `DbUpdateConcurrencyException`. |
| **14** | **Legacy Row Migration** | `[MODERN PRODUCT REQUIREMENT]` | Migration seeds a non-empty `Guid.NewGuid()` for all existing rows (`SELECT count(*) WHERE Version = Guid.Empty` must be 0). |
| **15** | **Zero Net Policy** | `[MODERN PRODUCT REQUIREMENT]` | Negative net is always rejected. Zero net is governed by `NetWeightPolicy` (`RejectZero` vs `AllowZero`), mapped from `Settings.AllowZeroNetWeight`. |
| **16** | **Gross / Tare Rules** | `[LEGACY VERIFIED]` | GrossFirst: $\text{Gross} = \text{First}, \text{Tare} = \text{Second}, \text{Net} = \text{Gross} - \text{Tare}$. TareFirst: $\text{Tare} = \text{First}, \text{Gross} = \text{Second}, \text{Net} = \text{Gross} - \text{Tare}$. Enforces $\text{Gross} \ge \text{Tare}$. |
| **17** | **Bag Deductions** | `[LEGACY VERIFIED]` | Stored: `NumberOfBags`, `BagWeightKg` (in integer grams). Calculated in domain: $\text{TotalBagWeightKg} = \text{Bags} \times \text{BagWeight}$, $\text{ActualWeightKg} = \text{Net} - \text{TotalBagWeight}$. No redundant DB columns. |
| **18** | **Charges Validation & Precision** | `[LEGACY VERIFIED]` | `Charges >= 0` and `SecondCharges >= 0` enforced. Stored losslessly in integer paise (`long ChargesPaise`, `long SecondChargesPaise`) with `AwayFromZero` rounding. |
| **19** | **Bag Non-Negativity** | `[LEGACY VERIFIED]` | `NumberOfBags >= 0` and `BagWeightKg >= 0` enforced. Null means not supplied. |
| **20** | **Negative Actual Weight Guard** | `[LEGACY VERIFIED]` | If bag deduction exceeds net weight ($\text{ActualWeightKg} < 0$), the domain strictly refuses the operation. |
| **21** | **Generic Custom Fields** | `[LEGACY VERIFIED]` | Custom fields remain generic strings in the domain; semantic labels (e.g. Consigner, Moisture %) are configured in `Settings` and print templates. |
| **22** | **Camera Decoupling** | `[MODERN PRODUCT REQUIREMENT]` | Camera capture and image attachment execute in background tasks; camera/network failure **never** invalidates a valid weighment transaction. |
| **23** | **Dual Migration Testing** | `[MODERN PRODUCT REQUIREMENT]` | Validated on both fresh database creation and upgrade from actual pre-Phase-2 schema (`20260826115121_HardenConstraintsAndAuditTrail`). |
| **24** | **F1 Ticket Timing** | `[MODERN PRODUCT REQUIREMENT]` | Ticket identity is allocated inside database transaction first $\rightarrow$ `WB-XXXXXX` displayed $\rightarrow$ first weight captured. No temporary fake numbers. |
| **25** | **Post-Completion Exclusion** | `[MODERN PRODUCT REQUIREMENT]` | Once `Status == WeighmentStatus.Completed`, the transaction is permanently excluded from F2 search, across application restarts. |
| **26** | **Keyboard Hotkey Architecture** | `[MODERN PRODUCT REQUIREMENT]` | `F1`, `F2`, `F3`, `F5`, `Esc` bound via WPF `KeyBinding` to `ICommand`, explicitly verified while focus is inside active text/combo input fields. |
| **27** | **No Dead Hotkeys** | `[MODERN PRODUCT REQUIREMENT]` | `F7` and `F12` are **not** implemented in Phase 2 because they have no defined business action. |
| **28** | **Security & Audit Pipeline** | `[MODERN PRODUCT REQUIREMENT]` | Every mutation passes: Authenticated Operator $\rightarrow$ Permission Check $\rightarrow$ Validation $\rightarrow$ Domain Invariant $\rightarrow$ Transaction $\rightarrow$ Audit Log $\rightarrow$ Post-Commit Event. No UI or hotkey bypass. |
| **29** | **Multi-File Documentation Rule** | `[MODERN PRODUCT REQUIREMENT]` | Every implementation gate must update `AI-Handoff.md`, `ImplementationStatus.md`, `Architecture.md`, `LegacyParitySpecification.md`, and `Development.md`. |

---

## 2. Structural Architecture & Component Workflows

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   F1 (First Entry Workflow)                                      │
│                                                                                                  │
│   Operator Presses F1                                                                            │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Form Initialization] ──► VehicleEntryViewModel resets form & auto-focuses Vehicle Number      │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Operator Input]      ──► Vehicle No + Mode (GrossFirst / TareFirst) + Party + Material        │
│                             + Vehicle Type + Driver + Transporter + Charges + Bags + F1 Custom   │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Capture Weight 1]    ──► Stable WeightReading from Scale Indicator (or F3 hotkey)             │
│          │                                                                                       │
│          ▼                                                                                       │
│   [IWeighmentService]   ──► CreateAsync() in single DB transaction:                              │
│                             - Allocates Slip Number (e.g. WB-000123) from allocated DB identity  │
│                             - Sets Status: AwaitingSecondWeight                                  │
│                             - Regenerates Version = Guid.NewGuid()                               │
│                             - Commits transaction & dispatches post-commit event                 │
│                             - 🔒 F1 HISTORICAL DATA BECOMES PERMANENTLY LOCKED                   │
│                             - Background Camera snapshot attached independently                  │
└────────────────────────────────────────────────┬─────────────────────────────────────────────────┘
                                                 │
                                                 │ Physical Vehicle Loading / Unloading
                                                 │ (Vehicle leaves platform & returns later)
                                                 │
                                                 ▼
┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   F2 (Second Entry Workflow)                                     │
│                                                                                                  │
│   Operator Presses F2                                                                            │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Pending F2 Lookup]   ──► IWeighmentService.FindPendingSecondEntryAsync(searchKey):            │
│                             - Matches ONLY Status == WeighmentStatus.AwaitingSecondWeight        │
│                             - Supports: Slip Number (WB-000123, 123), Vehicle Reg (MH12AB1234),   │
│                               or direct HUD queue item selection                                 │
│                             - Completed / Cancelled / Created transactions are EXCLUDED          │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Full Entity Restore] ──► Loads Full Weighment Aggregate (Bound by persistent Id + Version)    │
│                             - Restores exact textual F1 snapshot: Party, Material, Vehicle Type, │
│                               Driver, Transporter, First Weight, First Timestamp, F1 Operator    │
│                             - Prominently displays Ticket Number, Vehicle Reg & First Weight     │
│                             - UI renders locked fields with visual badge 🔒 From First Entry     │
│                             - Editable: Second Charges, Remarks, Gate Pass #, Bag adjustments,   │
│                               F2 Custom Fields (Custom 3 & 4)                                    │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Capture Weight 2]    ──► Stable WeightReading from Scale Indicator (or F3 hotkey)             │
│          │                                                                                       │
│          ▼                                                                                       │
│   [Mode-Aware Net Calc] ──► GrossFirst: Gross = First, Tare = Second, Net = Gross - Tare         │
│                             TareFirst:  Tare = First, Gross = Second, Net = Gross - Tare         │
│                             NetWeightPolicy: Negative -> Reject, Zero -> Allow/Reject by policy  │
│                             Actual Material Weight = Net - TotalBagWeight (Bags × BagWeight)     │
│          │                                                                                       │
│          ▼                                                                                       │
│   [IWeighmentService]   ──► RecordSecondWeightAsync(ActiveWeighmentId, Version, ...):            │
│                             - Operates strictly on persistent Id + Version (not textbox text)   │
│                             - Guards against rapid double-F5 completion (idempotent/guarded)     │
│                             - Sets Status: Completed & NetWeightGrams                            │
│                             - Regenerates Version = Guid.NewGuid()                               │
│                             - Commits transaction & dispatches post-commit event                 │
│                             - Background Camera snapshot attached independently                  │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Database Schema Delta & EF Core Migration Plan

### Schema Delta on `Weighments` Table:

| Column Name | SQLite Storage Type | C# Domain Property | Value Converter / Mapping | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| `Id` | `INTEGER` (PK) | `long Id` | None | Database allocated identity |
| `SlipNumber` | `TEXT` (Max 24, Unique) | `string SlipNumber` | None | Unique formatted slip number |
| `VehicleNumber` | `TEXT` (Max 24, Unique open) | `string VehicleNumber` | None | Canonical uppercase registration |
| `Mode` | `INTEGER` | `WeighmentMode Mode` | Enum to int | `GrossFirst` (0) / `TareFirst` (1) |
| `Status` | `INTEGER` | `WeighmentStatus Status`| Enum to int | `Created` (0), `AwaitingSecondWeight` (1), `Completed` (2), `Cancelled` (3) |
| `ChargesPaise` | `INTEGER` (`long`) | `decimal Charges` | `RupeesToPaise` (`paise / 100m`) | **[NEW]** F1 fee in integer paise |
| `SecondChargesPaise` | `INTEGER` (`long`) | `decimal SecondCharges` | `RupeesToPaise` (`paise / 100m`) | **[NEW]** F2 fee in integer paise |
| `NumberOfBags` | `INTEGER` (`int?`) | `int? NumberOfBags` | None | **[NEW]** Count of packaging bags ($\ge 0$) |
| `BagWeightGrams` | `INTEGER` (`long?`) | `decimal? BagWeightKg` | `KilogramsToGrams` (`grams / 1000m`)| **[NEW]** Empty bag tare weight ($\ge 0$) |
| `GatePassNumber` | `TEXT` (Max 64) | `string? GatePassNumber`| None | **[NEW]** Gate pass reference string |
| `CustomField1` | `TEXT` (Max 128) | `string? CustomField1` | None | **[NEW]** F1 Custom field 1 (locked in F2) |
| `CustomField2` | `TEXT` (Max 128) | `string? CustomField2` | None | **[NEW]** F1 Custom field 2 (locked in F2) |
| `CustomField3` | `TEXT` (Max 128) | `string? CustomField3` | None | **[NEW]** F2 Custom field 3 (editable in F2) |
| `CustomField4` | `TEXT` (Max 128) | `string? CustomField4` | None | **[NEW]** F2 Custom field 4 (editable in F2) |
| `Version` | `TEXT` (Guid) | `Guid Version` | `IsConcurrencyToken()` | **[NEW]** Application concurrency token |

---

## 4. Gated Execution Sequence for Phase 2

```text
Phase 2A: Legacy F1/F2 Behavioral Research & Specification (COMPLETED)
   │
   ▼
Phase 2B: Domain Aggregate Delta & Business Rules (Weighment.cs, NetWeightPolicy, Bags, Version, Lock F1)
   │
   ▼
Phase 2C: Persistence Delta & EF Core Migration (AddF1F2WorkflowFields, Fresh + Upgrade DB test)
   │
   ▼
Phase 2D: Application Service & Query Extensions (IWeighmentService.FindPendingSecondEntryAsync, UpdateSecondEntryDetailsAsync)
   │
   ▼
Phase 2E: WPF UI Modernization, Hotkeys & Queue UX (VehicleEntryView.xaml tabs, locked fields UX)
   │
   ▼
Phase 2F: Automated Test Suite & Real Process Crash-Recovery Verification (660+ tests, PowerShell crash test)
```

---

## 5. Exhaustive Test Matrix

### A. Domain Unit Tests (`WeighmentDomainTests.cs`)
1. `Weighment_Open_Initializes_Status_Created_And_Zero_Net`
2. `Weighment_RecordFirstWeight_Transitions_To_AwaitingSecondWeight`
3. `Weighment_AfterFirstWeight_F1DetailsCannotBeChanged` (Asserts `UpdateDetails` throws in `AwaitingSecondWeight`)
4. `Weighment_UpdateSecondEntryDetails_Requires_AwaitingSecondWeight` (Throws in `Created`, `Completed`, `Cancelled`)
5. `Weighment_RecordSecondWeight_GrossFirst_PositiveNet_Calculates_Correct_Net`
6. `Weighment_RecordSecondWeight_TareFirst_PositiveNet_Calculates_Correct_Net`
7. `Weighment_RecordSecondWeight_NegativeNet_Throws_InvalidOperationException`
8. `Weighment_RecordSecondWeight_ZeroNet_When_Disallowed_Throws_InvalidOperationException`
9. `Weighment_RecordSecondWeight_ZeroNet_When_Allowed_Succeeds`
10. `Weighment_BagDeductions_Calculates_TotalBagWeight_And_ActualWeight_Correctly`
11. `Weighment_BagDeductions_Exceeding_Net_Throws_InvalidOperationException`
12. `Weighment_VersionToken_Changes_On_Every_State_Transition`

### B. Service & Concurrency Tests (`WeighmentServiceTests.cs`)
1. `Service_FindPendingSecondEntryAsync_Resolves_Only_AwaitingSecondWeight`
2. `Service_FindPendingSecondEntryAsync_Excludes_Completed_Cancelled_And_Created_Weighments`
3. `Service_Concurrent_Duplicate_Open_Same_Vehicle_Exactly_One_Succeeds` (100 concurrent requests)
4. `Service_Concurrent_Distinct_Vehicles_All_Succeed_With_Monotonic_SlipNumbers` (100 concurrent requests)
5. `Service_OptimisticConcurrency_Throws_DbUpdateConcurrencyException_On_Stale_Version_Save`
6. `Service_RoleBasedAuthorization_Enforces_Permissions_Across_Roles` (ReadOnly, Operator, Supervisor, Admin)

### C. Persistence & Migration Tests (`WeighBridgeDbContextTests.cs`)
1. `Database_Fresh_Creation_Includes_All_New_Columns`
2. `Database_Existing_Upgrade_Applies_Migration_From_PrePhase2_Schema_Cleanly` (Verifies non-empty Version seed)
3. `Database_Weights_And_Charges_RoundTrip_Losslessly_In_Integer_Grams_And_Paise`

### D. Presentation & UI Tests (`VehicleEntryViewModelTests.cs`)
1. `ViewModel_F2Search_Binds_ActiveWeighmentId_And_Version_Directly`
2. `ViewModel_Rapid_Double_F5_Executes_Completion_Only_Once`
3. `ViewModel_Esc_Clears_Active_Transaction_Context_Without_Modifying_Database`
4. `ViewModel_RealWPF_Hotkeys_Execute_While_Focus_In_TextBox`
5. `ViewModel_RealWPF_F1Fields_Cannot_Be_Edited_In_AwaitingSecondWeight`

### E. Real Process Boundary Crash-Recovery Test (`scripts/f1-f2-crash-recovery-smoke.ps1`)
1. **Process 1 (F1):** Launch app $\rightarrow$ Open F1 weighment $\rightarrow$ Capture first weight $\rightarrow$ Verify ticket `WB-XXXXXX` saved in SQLite $\rightarrow$ Kill process (`Stop-Process -Force`).
2. **Process 2 (F2):** Relaunch app $\rightarrow$ Enter F2 mode $\rightarrow$ Search ticket `WB-XXXXXX` $\rightarrow$ Verify 100% of F1 fields restored and locked $\rightarrow$ Capture second weight $\rightarrow$ Verify Net calculation $\rightarrow$ Complete weighment $\rightarrow$ Verify `Status = Completed` persisted in SQLite.
3. **Post-Completion Check:** Relaunch app $\rightarrow$ Search ticket `WB-XXXXXX` in F2 $\rightarrow$ Verify `FindPendingSecondEntryAsync` returns null.
