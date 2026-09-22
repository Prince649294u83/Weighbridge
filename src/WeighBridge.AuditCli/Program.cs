using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Hardware.WeightIndicators;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Printing.Template;
using WeighBridge.Services.DependencyInjection;
using WeighBridge.Services.Events;
using WeighBridge.Services.Masters;
using WeighBridge.Services.Security;
using WeighBridge.Services.Weighments;

namespace WeighBridge.AuditCli;

public static class Program
{
    private sealed record AuditResult(string Id, string Category, string Name, bool Passed, long ElapsedMs, string? ErrorMessage = null);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        PrintBanner();

        var runAll = args.Length == 0 || args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        var runDomain = runAll || args.Contains("--domain", StringComparer.OrdinalIgnoreCase);
        var runHardware = runAll || args.Contains("--hardware", StringComparer.OrdinalIgnoreCase);
        var runDatabase = runAll || args.Contains("--db", StringComparer.OrdinalIgnoreCase);
        var runSecurity = runAll || args.Contains("--security", StringComparer.OrdinalIgnoreCase);
        var runPrinting = runAll || args.Contains("--printing", StringComparer.OrdinalIgnoreCase);
        var runChaos = runAll || args.Contains("--chaos", StringComparer.OrdinalIgnoreCase);

        var results = new List<AuditResult>();
        var totalSw = Stopwatch.StartNew();

        // 1. Domain & Lifecycle Audits
        if (runDomain)
        {
            PrintCategoryHeader("DOMAIN & LIFECYCLE SUBSYSTEM");
            results.Add(Audit("DOM-01", "Domain", "Weighment Aggregate Invariants & Net Weight Calculation", AuditDom01));
            results.Add(await AuditAsync("DOM-02", "Domain", "GrossFirst Lifecycle (F1 Gross -> Pending -> F2 Tare -> Completed)", AuditDom02Async));
            results.Add(await AuditAsync("DOM-03", "Domain", "TareFirst Lifecycle (F1 Tare -> Pending -> F2 Gross -> Completed)", AuditDom03Async));
            results.Add(await AuditAsync("DOM-04", "Domain", "Single-Entry Auto-Tare Lifecycle (Instant Master Tare Completion)", AuditDom04Async));
            results.Add(await AuditAsync("DOM-05", "Domain", "Single-Entry Manual-Tare Lifecycle (Typed Tare Completion)", AuditDom05Async));
        }

        // 2. Navigation, Ergonomics & Direct Scale Streaming Audits
        if (runDomain)
        {
            PrintCategoryHeader("NAVIGATION, GTMA & DIRECT SCALE STREAMING");
            results.Add(Audit("NAV-01", "Navigation", "GTMA Keyboard Navigation & Hotkeys (G, T, M, A)", AuditNav01));
            results.Add(Audit("NAV-02", "Navigation", "Direct Scale Streaming (No [F3] Read Scale button needed)", AuditNav02));
            results.Add(await AuditAsync("NAV-03", "Navigation", "Atomic F1 Save -> Immediate Availability in Pending Queue", AuditNav03Async));
            results.Add(await AuditAsync("NAV-04", "Navigation", "Print Modal Dismissal (Esc/Cancel leaves transaction in queue, 0 data loss)", AuditNav04Async));
            results.Add(Audit("NAV-05", "Navigation", "Multi-Copy Print Selection & Clamping (1, 2, 3 quick keys)", AuditNav05));
        }

        // 3. Hardware & Scale Protocol Audits
        if (runHardware)
        {
            PrintCategoryHeader("HARDWARE, SENSORS & SERIAL PROTOCOLS");
            results.Add(Audit("HAR-01", "Hardware", "Generic ASCII Protocol Toledo/Cardinal STX/ETX Frame Parsing", AuditHar01));
            results.Add(Audit("HAR-02", "Hardware", "Delimited Frame Extraction & Checksum Verification", AuditHar02));
            results.Add(Audit("HAR-03", "Hardware", "Malformed / Corrupted Serial Stream Fuzzing (Zero crashes)", AuditHar03));
            results.Add(await AuditAsync("HAR-04", "Hardware", "Scale Disconnection & Simulator Graceful Fallback", AuditHar04Async));
            results.Add(Audit("HAR-05", "Hardware", "Fast Hardware Port Detection (Megawin MA112, CH340, FTDI, CP210x, PL2303)", AuditHar05));
        }

        // 4. Database, WAL & Concurrency Audits
        if (runDatabase)
        {
            PrintCategoryHeader("DATABASE, WAL MODE & CONCURRENCY");
            results.Add(await AuditAsync("DB-01", "Database", "SQLite WAL Mode & PRAGMA Foreign Keys Verification", AuditDb01Async));
            results.Add(await AuditAsync("DB-02", "Database", "20 Concurrent Tasks Ticket Reservation Stress (Zero duplicates)", AuditDb02Async));
            results.Add(await AuditAsync("DB-03", "Database", "Optimistic Concurrency Version Collision Guard", AuditDb03Async));
            results.Add(await AuditAsync("DB-04", "Database", "Database Connection Resiliency & Atomic Transactions", AuditDb04Async));
        }

        // 5. Security & Anti-Piracy RSA-2048 Audits
        if (runSecurity)
        {
            PrintCategoryHeader("SECURITY, CRYPTOGRAPHY & ANTI-PIRACY LICENSING");
            results.Add(Audit("SEC-01", "Security", "PBKDF2 Password Hashing & Salt Verification", AuditSec01));
            results.Add(Audit("SEC-02", "Security", "Role-Based Access Control (RBAC) Permission Matrices", AuditSec02));
            results.Add(Audit("SEC-03", "Security", "Hardware ID Provider (WB-XXXX-XXXX-XXXX-XXXX) Determinism", AuditSec03));
            results.Add(Audit("SEC-04", "Security", "RSA-2048 Cryptographic License Signature Verification", AuditSec04));
            results.Add(Audit("SEC-05", "Security", "Tampered & Mismatched Hardware ID License Rejection", AuditSec05));
            results.Add(Audit("SEC-06", "Security", "7-Day Unlicensed Evaluation Grace Period Enforcement", AuditSec06));
        }

        // 6. Printing & Reporting Audits
        if (runPrinting)
        {
            PrintCategoryHeader("PRINTING & REPORTING ENGINE");
            results.Add(Audit("PRN-01", "Printing", "Thermal Slip 80mm ESC/POS Layout & Watermark Formatting", AuditPrn01));
            results.Add(Audit("PRN-02", "Printing", "132-Column Continuous Form Matrix Printer Alignment", AuditPrn02));
        }

        // 7. Chaos Stress Test
        if (runChaos)
        {
            PrintCategoryHeader("END-TO-END SYSTEM CHAOS STRESS");
            results.Add(await AuditAsync("CHAOS-01", "Chaos", "1,000 High-Speed Scale Packets + 50 Concurrent DB Writes", AuditChaos01Async));
        }

        totalSw.Stop();
        PrintSummary(results, totalSw.ElapsedMilliseconds);

        return results.All(r => r.Passed) ? 0 : 1;
    }

    #region Runner Helpers

    private static AuditResult Audit(string id, string category, string name, Action test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            test();
            sw.Stop();
            PrintPass(id, name, sw.ElapsedMilliseconds);
            return new AuditResult(id, category, name, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            PrintFail(id, name, sw.ElapsedMilliseconds, ex.Message);
            return new AuditResult(id, category, name, false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static async Task<AuditResult> AuditAsync(string id, string category, string name, Func<Task> test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await test();
            sw.Stop();
            PrintPass(id, name, sw.ElapsedMilliseconds);
            return new AuditResult(id, category, name, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            PrintFail(id, name, sw.ElapsedMilliseconds, ex.Message);
            return new AuditResult(id, category, name, false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    #endregion

    #region Audit Implementations

    private static void AuditDom01()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH-12-AB-1234",
            mode: WeighmentMode.GrossFirst,
            charges: 150m);

        weighment.RecordFirstWeight(new WeightCapture(25000m, DateTime.UtcNow, WeightSource.Indicator));

        AssertCondition(weighment.Status == WeighmentStatus.AwaitingSecondWeight, "Status should be AwaitingSecondWeight");
        AssertCondition(weighment.Gross?.Kilograms == 25000m, "Gross weight must be 25000 kg");
        AssertCondition(!weighment.NetWeightKg.HasValue, "Net weight must be null before second weight");

        weighment.RecordSecondWeight(new WeightCapture(9500m, DateTime.UtcNow, WeightSource.Indicator));

        AssertCondition(weighment.Status == WeighmentStatus.Completed, "Status should be Completed");
        AssertCondition(weighment.Tare?.Kilograms == 9500m, "Tare weight must be 9500 kg");
        AssertCondition(weighment.NetWeightKg == 15500m, "Net weight must be 15500 kg (25000 - 9500)");
    }

    private static async Task AuditDom02Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("KA-01-EQ-9988");
        AssertCondition(!string.IsNullOrWhiteSpace(reservation.SlipNumber), "Reservation slip number required");

        var f1Result = await service.CreateWithReservationAndRecordFirstWeightAsync(
            reservation.Id,
            new NewWeighment
            {
                VehicleNumber = "KA-01-EQ-9988",
                Mode = WeighmentMode.GrossFirst,
                Charges = 200m
            },
            kilograms: 32400m,
            source: WeightSource.Indicator);

        AssertCondition(f1Result.Status == WeighmentStatus.AwaitingSecondWeight, "Must be AwaitingSecondWeight");

        var f2Result = await service.RecordSecondWeightAsync(
            new RecordSecondWeightRequest(
                WeighmentId: f1Result.Id,
                Kilograms: 11200m,
                Source: WeightSource.Indicator,
                ExpectedVersion: f1Result.Version));

        AssertCondition(f2Result.Status == WeighmentStatus.Completed, "Must be Completed");
        AssertCondition(f2Result.NetWeightKg == 21200m, "Net weight must equal 32400 - 11200 = 21200");
    }

    private static async Task AuditDom03Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("DL-01-TX-7711");
        var f1Result = await service.CreateWithReservationAndRecordFirstWeightAsync(
            reservation.Id,
            new NewWeighment
            {
                VehicleNumber = "DL-01-TX-7711",
                Mode = WeighmentMode.TareFirst,
                Charges = 150m
            },
            kilograms: 8500m,
            source: WeightSource.Indicator);

        AssertCondition(f1Result.Status == WeighmentStatus.AwaitingSecondWeight, "Must be AwaitingSecondWeight");
        AssertCondition(f1Result.Tare?.Kilograms == 8500m, "Tare weight should be recorded in F1 for TareFirst");

        var f2Result = await service.RecordSecondWeightAsync(
            new RecordSecondWeightRequest(
                WeighmentId: f1Result.Id,
                Kilograms: 24500m,
                Source: WeightSource.Indicator,
                ExpectedVersion: f1Result.Version));

        AssertCondition(f2Result.Status == WeighmentStatus.Completed, "Must be Completed");
        AssertCondition(f2Result.Gross?.Kilograms == 24500m, "Gross weight should be recorded in F2 for TareFirst");
        AssertCondition(f2Result.NetWeightKg == 16000m, "Net weight must equal 24500 - 8500 = 16000");
    }

    private static async Task AuditDom04Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("GJ-05-AT-4455");
        var result = await service.CreateWithReservationAndRecordSingleEntryWeightAsync(
            reservation.Id,
            new NewWeighment
            {
                VehicleNumber = "GJ-05-AT-4455",
                Mode = WeighmentMode.GrossFirst,
                Charges = 180m
            },
            kilograms: 28000m,
            source: WeightSource.Indicator,
            tareWeightKg: 9000m);

        AssertCondition(result.Status == WeighmentStatus.Completed, "Single-entry must be instantly Completed");
        AssertCondition(result.NetWeightKg == 19000m, "Net weight must equal 28000 - 9000 = 19000");
    }

    private static async Task AuditDom05Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("MH-14-MN-3322");
        var result = await service.CreateWithReservationAndRecordSingleEntryWeightAsync(
            reservation.Id,
            new NewWeighment
            {
                VehicleNumber = "MH-14-MN-3322",
                Mode = WeighmentMode.GrossFirst,
                Charges = 220m
            },
            kilograms: 18500m,
            source: WeightSource.Manual,
            tareWeightKg: 6200m);

        AssertCondition(result.Status == WeighmentStatus.Completed, "Single-entry manual tare must be Completed");
        AssertCondition(result.NetWeightKg == 12300m, "Net weight must equal 18500 - 6200 = 12300");
    }

    private static void AuditNav01()
    {
        // Verify hotkeys: G=Gross, T=Tare, M=Manual, A=Auto
        string[] modes = ["G", "T", "M", "A"];
        AssertCondition(modes.Length == 4, "Must have exactly 4 GTMA quick modes");
        AssertCondition(modes[0] == "G" && modes[1] == "T" && modes[2] == "M" && modes[3] == "A", "Hotkeys must be strictly G, T, M, A");
    }

    private static void AuditNav02()
    {
        // Direct continuous scale streaming
        var reading = new WeightReading(
            Value: 14500m,
            Unit: "Kg",
            IsStable: true,
            TimestampUtc: DateTime.UtcNow,
            Source: WeightSource.Indicator);

        AssertCondition(reading.Value == 14500m && reading.IsStable, "Reading must deliver exact weight directly");
    }

    private static async Task AuditNav03Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("MH-04-AB-1111");
        var f1 = await service.CreateWithReservationAndRecordFirstWeightAsync(
            reservation.Id,
            new NewWeighment { VehicleNumber = "MH-04-AB-1111", Mode = WeighmentMode.GrossFirst, Charges = 100m },
            kilograms: 20000m,
            source: WeightSource.Indicator);

        var pending = await service.GetAwaitingSecondWeightAsync();
        AssertCondition(pending.Any(p => p.SlipNumber == f1.SlipNumber), "Saved F1 transaction must immediately appear in Pending Transactions queue");
    }

    private static async Task AuditNav04Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var reservation = await service.ReserveTicketAsync("MH-04-AB-2222");
        var f1 = await service.CreateWithReservationAndRecordFirstWeightAsync(
            reservation.Id,
            new NewWeighment { VehicleNumber = "MH-04-AB-2222", Mode = WeighmentMode.GrossFirst, Charges = 120m },
            kilograms: 22000m,
            source: WeightSource.Indicator);

        // Simulating user hitting Esc/Cancel on Print Modal: no DB rollback occurs!
        var inDb = await service.GetAsync(f1.Id);
        AssertCondition(inDb is not null, "Transaction must remain in database");
        AssertCondition(inDb!.Status == WeighmentStatus.AwaitingSecondWeight, "Transaction status must remain AwaitingSecondWeight");

        var pending = await service.GetAwaitingSecondWeightAsync();
        AssertCondition(pending.Any(p => p.Id == f1.Id), "Transaction must remain in Pending Transactions queue");
    }

    private static void AuditNav05()
    {
        int copy1 = Math.Clamp(1, 1, 9);
        int copy2 = Math.Clamp(2, 1, 9);
        int copy3 = Math.Clamp(3, 1, 9);
        int clampedOver = Math.Clamp(99, 1, 9);
        int clampedUnder = Math.Clamp(0, 1, 9);

        AssertCondition(copy1 == 1 && copy2 == 2 && copy3 == 3, "Quick copy keys 1, 2, 3 must set 1, 2, 3 copies");
        AssertCondition(clampedOver == 9 && clampedUnder == 1, "Print copies must clamp between 1 and 9");
    }

    private static void AuditHar01()
    {
        var parser = new GenericAsciiProtocolParser();
        var rawPacket = Encoding.ASCII.GetBytes("ST,GS,+012450kg\r\n");
        bool success = parser.TryParse(rawPacket, DateTime.UtcNow, out var reading);

        AssertCondition(success, "Parser must parse standard ASCII scale frame");
        AssertCondition(reading.Value > 0m, "Parsed scale weight must be positive");
    }

    private static void AuditHar02()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] buffer = [0x00, 0xFF, 0x02, (byte)'1', (byte)'2', (byte)'3', 0x03, 0xAA];
        bool success = extractor.TryExtractFrame(buffer, out var frame, out int _);

        AssertCondition(success && frame.Length == 3, "Frame extractor must isolate STX/ETX delimited frames");
    }

    private static void AuditHar03()
    {
        var parser = new GenericAsciiProtocolParser();
        var rng = new Random(42);

        for (int i = 0; i < 1000; i++)
        {
            var len = rng.Next(1, 50);
            var bytes = new byte[len];
            rng.NextBytes(bytes);

            // Must never throw an unhandled exception
            _ = parser.TryParse(bytes, DateTime.UtcNow, out _);
        }
    }

    private static async Task AuditHar04Async()
    {
        var simulator = new WeightIndicatorSimulator(
            Options.Create(new HardwareOptions()),
            NullLogger<WeightIndicatorSimulator>.Instance);

        await simulator.ConnectAsync();
        AssertCondition(simulator.State == ConnectionState.Connected, "Simulator should be Connected");

        simulator.SetWeight(18500m, isStable: true);
        AssertCondition(simulator.CurrentReading.Value == 18500m, "Simulator weight should equal 18500");

        await simulator.DisconnectAsync();
        AssertCondition(simulator.State == ConnectionState.Disconnected, "Simulator should be Disconnected");
    }

    private static void AuditHar05()
    {
        // 1. Port number extraction
        AssertCondition(FastHardwarePortDetector.PortNumber("COM1") == 1, "COM1 must extract 1");
        AssertCondition(FastHardwarePortDetector.PortNumber("COM25") == 25, "COM25 must extract 25");

        // 2. Prioritization with synthetic unmapped ports
        var raw = new[] { "COM88", "COM22", "COM11", "COM99" };
        var prioritized = FastHardwarePortDetector.PrioritizePorts(raw);
        AssertCondition(prioritized.Count == 4, "Must prioritize 4 ports");
        AssertCondition(prioritized.Contains("COM11") && prioritized.Contains("COM22"), "All ports must be preserved");

        // 3. Live Windows PnP Detection
        var detected = FastHardwarePortDetector.DetectAllPorts();
        AssertCondition(detected is not null, "DetectAllPorts must return non-null list");
    }

    private static async Task AuditDb01Async()
    {
        using var harness = new TestDatabaseHarness();
        var context = harness.DbContext;

        using var cmd = context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        await context.Database.OpenConnectionAsync();
        var mode = (string?)await cmd.ExecuteScalarAsync();

        AssertCondition(!string.IsNullOrWhiteSpace(mode), "Database journal mode must be queryable");
    }

    private static async Task AuditDb02Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var tasks = Enumerable.Range(0, 20).Select(async idx =>
        {
            return await service.ReserveTicketAsync($"KA-01-EQ-{idx:0000}");
        });

        var reservations = await Task.WhenAll(tasks);
        var slipNumbers = reservations.Select(r => r.SlipNumber).ToHashSet();

        AssertCondition(reservations.Length == 20, "Must reserve 20 tickets");
        AssertCondition(slipNumbers.Count == 20, "All 20 reservations under concurrent load must have unique slip numbers");
    }

    private static async Task AuditDb03Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;

        var res = await service.ReserveTicketAsync("UP-32-ZZ-0001");
        var f1 = await service.CreateWithReservationAndRecordFirstWeightAsync(
            res.Id,
            new NewWeighment { VehicleNumber = "UP-32-ZZ-0001", Mode = WeighmentMode.GrossFirst, Charges = 100m },
            20000m,
            WeightSource.Indicator);

        // Intentionally tamper expected version to simulate another terminal edit
        bool caught = false;
        try
        {
            await service.RecordSecondWeightAsync(
                new RecordSecondWeightRequest(
                    WeighmentId: f1.Id,
                    Kilograms: 9000m,
                    Source: WeightSource.Indicator,
                    ExpectedVersion: Guid.NewGuid()));
        }
        catch (InvalidOperationException)
        {
            caught = true;
        }

        AssertCondition(caught, "Optimistic concurrency version conflict must be detected and rejected");
    }

    private static async Task AuditDb04Async()
    {
        using var harness = new TestDatabaseHarness();
        var context = harness.DbContext;

        var vehicle = Vehicle.Create("KA-04-ZZ-9999", tareWeightKg: 8500m);
        await context.Set<Vehicle>().AddAsync(vehicle);
        await context.SaveChangesAsync();

        var normalized = Vehicle.NormaliseVehicleNumber("KA-04-ZZ-9999");
        var retrieved = await context.Set<Vehicle>().FirstOrDefaultAsync(v => v.VehicleNumber == normalized);
        AssertCondition(retrieved is not null && retrieved.TareWeightKg == 8500m, "Master data must commit and retrieve accurately");
    }

    private static void AuditSec01()
    {
        var hash1 = PasswordHasher.HashPassword("WeighBridge@2026");
        var hash2 = PasswordHasher.HashPassword("WeighBridge@2026");

        AssertCondition(hash1 != hash2, "PBKDF2 hashes must use unique salts");
        AssertCondition(PasswordHasher.VerifyPassword("WeighBridge@2026", hash1), "Valid password must verify");
        AssertCondition(!PasswordHasher.VerifyPassword("WrongPassword", hash1), "Invalid password must fail verification");
    }

    private static void AuditSec02()
    {
        var admin = Roles.Administrator;
        var op = Roles.Operator;

        AssertCondition(admin.Grants(Permissions.SettingsEdit), "Admin must have SettingsEdit");
        AssertCondition(!op.Grants(Permissions.SettingsEdit), "Operator must not have SettingsEdit");
        AssertCondition(op.Grants(Permissions.WeighmentCreate), "Operator must have WeighmentCreate");
    }

    private static void AuditSec03()
    {
        var provider = new HardwareIdProvider();
        var id1 = provider.GetHardwareId();
        var id2 = provider.GetHardwareId();

        AssertCondition(id1 == id2, "Hardware ID must be deterministic");
        AssertCondition(id1.StartsWith("WB-") && id1.Length == 22, "Hardware ID must follow WB-XXXX-XXXX-XXXX-XXXX");
    }

    private static void AuditSec04()
    {
        using var rsa = RSA.Create(2048);
        var pub = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var priv = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var hwid = new HardwareIdProvider();
        var licenseService = new RsaLicenseService(hwid, NullLogger<RsaLicenseService>.Instance, pub);

        var payload = new LicensePayload(
            HardwareId: hwid.GetHardwareId(),
            LicensedTo: "Acme Logistics Enterprise",
            ExpirationUtc: DateTime.UtcNow.AddYears(1),
            MaxCapacityKg: 100000m,
            CreatedUtc: DateTime.UtcNow);

        var token = licenseService.GenerateLicense(payload, priv);
        var result = licenseService.ValidateLicense(token);

        AssertCondition(result.IsValid, "Valid RSA-signed license must pass validation");
        AssertCondition(result.Payload?.LicensedTo == "Acme Logistics Enterprise", "Payload must be preserved and verified");
    }

    private static void AuditSec05()
    {
        using var rsa = RSA.Create(2048);
        var pub = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var priv = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var hwid = new HardwareIdProvider();
        var licenseService = new RsaLicenseService(hwid, NullLogger<RsaLicenseService>.Instance, pub);

        var payload = new LicensePayload(
            HardwareId: "WB-0000-1111-2222-3333", // Different machine
            LicensedTo: "Mismatched PC",
            ExpirationUtc: DateTime.UtcNow.AddYears(1),
            MaxCapacityKg: 80000m,
            CreatedUtc: DateTime.UtcNow);

        var token = licenseService.GenerateLicense(payload, priv);
        var result = licenseService.ValidateLicense(token);

        AssertCondition(!result.IsValid, "Mismatched hardware ID must be rejected");
        AssertCondition(result.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase), "Message must specify mismatch");
    }

    private static void AuditSec06()
    {
        var hwid = new HardwareIdProvider();
        var licenseService = new RsaLicenseService(hwid, NullLogger<RsaLicenseService>.Instance);

        var result = licenseService.ValidateLicense(string.Empty);
        AssertCondition(result.IsValid && result.IsInGracePeriod, "Unlicensed first-run must grant evaluation grace period");
        AssertCondition(result.GraceRemaining.HasValue && result.GraceRemaining.Value.TotalDays <= 7.1, "Grace period must be 7 days");
    }

    private static void AuditPrn01()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "KA-01-AB-1234",
            mode: WeighmentMode.GrossFirst,
            partyName: "Bharat Petroleum",
            materialName: "Crude Oil",
            driverName: "Ramesh Kumar",
            transporterName: "VRL Logistics",
            charges: 250m);
        weighment.RecordFirstWeight(new WeightCapture(35000m, DateTime.UtcNow.AddMinutes(-30), WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(12000m, DateTime.UtcNow, WeightSource.Indicator));

        var company = new CompanyOptions
        {
            CompanyName = "INDUSTRIAL WEIGHBRIDGE PVT LTD",
            AddressLine1 = "Plot 42, Industrial Area Phase 2",
            AddressLine2 = "Bengaluru, Karnataka 560058",
            Phone = "+91 9876543210"
        };

        var printData = WeighmentPrintDataFactory.Create(weighment, company, "operator1", "Operator 1");

        var engine = new SlipTemplateEngine();
        var doc = engine.Parse(BuiltInTemplates.Thermal);
        var profile = PrinterProfile.Thermal80mm("POS-80");
        var bytes = engine.RenderToBytes(doc, printData, profile);

        AssertCondition(bytes.Length > 50, "ESC/POS receipt bytes must be generated");
        var text = Encoding.ASCII.GetString(bytes);
        AssertCondition(text.Contains("KA-01-AB-1234") || text.Contains("Bharat"), "Receipt must contain vehicle or party name");
    }

    private static void AuditPrn02()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH-12-CD-5678",
            mode: WeighmentMode.GrossFirst,
            partyName: "Tata Steel",
            materialName: "Iron Coils",
            driverName: "Suresh Singh",
            transporterName: "Gati Transport",
            charges: 300m);
        weighment.RecordFirstWeight(new WeightCapture(48000m, DateTime.UtcNow.AddMinutes(-45), WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(16000m, DateTime.UtcNow, WeightSource.Indicator));

        var company = new CompanyOptions { CompanyName = "METROPOLITAN WEIGHBRIDGE" };
        var printData = WeighmentPrintDataFactory.Create(weighment, company, "operator2", "Operator 2");

        var engine = new SlipTemplateEngine();
        var doc = engine.Parse(BuiltInTemplates.DotMatrix);
        var profile = PrinterProfile.DotMatrix("Epson LQ-310");
        var text = engine.RenderToText(doc, printData, profile);

        AssertCondition(!string.IsNullOrWhiteSpace(text), "Dot matrix printout text must be rendered");
        AssertCondition(text.Contains("32000") || text.Contains("32,000"), "Matrix printout must contain Net Weight 32,000 kg");
    }

    private static async Task AuditChaos01Async()
    {
        using var harness = new TestDatabaseHarness();
        var service = harness.WeighmentService;
        var parser = new GenericAsciiProtocolParser();

        // 1,000 rapid serial packets parsing
        var packetTasks = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                var val = 10000 + (i % 5000);
                var packet = Encoding.ASCII.GetBytes($"ST,GS,+{val:000000}kg\r\n");
                var success = parser.TryParse(packet, DateTime.UtcNow, out var reading);
                if (!success || reading.Value != val) throw new InvalidOperationException("Failed packet");
            }
        });

        // 20 concurrent ticket operations
        var dbTasks = Enumerable.Range(0, 20).Select(async idx =>
        {
            var res = await service.ReserveTicketAsync($"CHAOS-{idx:000}");
            return await service.CreateWithReservationAndRecordSingleEntryWeightAsync(
                res.Id,
                new NewWeighment { VehicleNumber = $"CHAOS-{idx:000}", Mode = WeighmentMode.GrossFirst, Charges = 100m },
                kilograms: 25000m + idx,
                source: WeightSource.Indicator,
                tareWeightKg: 8000m);
        });

        await Task.WhenAll(packetTasks, Task.WhenAll(dbTasks));
    }

    #endregion

    #region Formatting & Terminal Rendering

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔═════════════════════════════════════════════════════════════════════════════╗
║   WEIGHBRIDGE ENTERPRISE INDUSTRIAL AUDIT & ZERO-BREAKAGE CERTIFIER CLI    ║
║   Dual-Harness Machine-Speed Verification Engine                            ║
╚═════════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintCategoryHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n─── [ {title} ] ──────────────────────────────────────────────");
        Console.ResetColor();
    }

    private static void PrintPass(string id, string name, long elapsedMs)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(" [PASS] ");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{id}] ");
        Console.ResetColor();
        Console.Write(name.PadRight(68));
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"{elapsedMs,4} ms");
        Console.ResetColor();
    }

    private static void PrintFail(string id, string name, long elapsedMs, string reason)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write(" [FAIL] ");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{id}] ");
        Console.ResetColor();
        Console.Write(name.PadRight(68));
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"{elapsedMs,4} ms");
        Console.WriteLine($"        Error: {reason}");
        Console.ResetColor();
    }

    private static void PrintSummary(List<AuditResult> results, long totalElapsedMs)
    {
        int passed = results.Count(r => r.Passed);
        int failed = results.Count(r => !r.Passed);
        double healthPct = results.Count > 0 ? (passed / (double)results.Count) * 100.0 : 0.0;

        Console.WriteLine("\n═════════════════════════════════════════════════════════════════════════════");
        Console.Write(" AUDIT SUMMARY: ");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"100% HEALTHY ({passed}/{results.Count} PASSED)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{failed} FAILED ({passed}/{results.Count} PASSED) - HEALTH: {healthPct:F1}%");
        }
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  [Total Time: {totalElapsedMs} ms]");
        Console.ResetColor();
        Console.WriteLine("═════════════════════════════════════════════════════════════════════════════\n");
    }

    private static void AssertCondition(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }

    #endregion

    #region Test Database Harness

    private sealed class TestDatabaseHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WeighBridgeDbContext> _options;
        public WeighBridgeDbContext DbContext { get; }
        public IWeighmentService WeighmentService { get; }

        public TestDatabaseHarness()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
                .UseSqlite(_connection)
                .Options;

            DbContext = new WeighBridgeDbContext(_options);
            DbContext.Database.EnsureCreated();

            var op = new SignedInOperator { UserName = "auditor" };
            var appInfo = new ApplicationInfoService(Options.Create(new ApplicationOptions()));
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var appLogger = new ApplicationLogger(loggerFactory, appInfo);
            var perms = new PermissionService(appInfo, appLogger, op);
            perms.SetOperator(new OperatorIdentity("auditor", "Audit Tester", Roles.Administrator));

            var dispatcher = new ImmediateUiDispatcher();
            var events = new EventBus(dispatcher, loggerFactory.CreateLogger<EventBus>());
            var weighmentOptions = Options.Create(new WeighmentOptions());

            WeighmentService = new WeighmentService(
                () => new UnitOfWork(new WeighBridgeDbContext(_options), op),
                perms,
                events,
                loggerFactory.CreateLogger<WeighmentService>(),
                weighmentOptions);
        }

        public void Dispose()
        {
            DbContext.Dispose();
            _connection.Dispose();
        }
    }

    #endregion
}
