using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Services.Import;

/// <summary>
/// Production legacy data importer supporting Microsoft Access (.mdb, .accdb) via ODBC
/// as well as CSV archives for migration of historical masters and weighment records.
/// </summary>
public sealed class MdbLegacyImporter : ILegacyDataImporter
{
    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly ILogger<MdbLegacyImporter> _logger;

    public MdbLegacyImporter(
        Func<IUnitOfWork> unitOfWork,
        ILogger<MdbLegacyImporter> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LegacyImportResult> ImportAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Import file not found: {filePath}", filePath);
        }

        var errors = new List<string>();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext is ".csv" or ".txt")
        {
            return await ImportCsvAsync(filePath, progress, cancellationToken).ConfigureAwait(false);
        }

        if (ext is not (".mdb" or ".accdb"))
        {
            throw new NotSupportedException($"Unsupported file format: {ext}. Supported formats are .mdb, .accdb, and .csv.");
        }

        return await ImportMdbAsync(filePath, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LegacyImportResult> ImportMdbAsync(
        string filePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        int partiesCount = 0;
        int materialsCount = 0;
        int vehiclesCount = 0;
        int weighmentsCount = 0;

        string[] connStrings =
        [
            $"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};Dbq={filePath};",
            $"Driver={{Microsoft Access Driver (*.mdb)}};Dbq={filePath};"
        ];

        OdbcConnection? connection = null;
        Exception? lastEx = null;

        foreach (var cs in connStrings)
        {
            try
            {
                var conn = new OdbcConnection(cs);
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                connection = conn;
                break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        if (connection == null)
        {
            var msg = "Unable to connect to Microsoft Access database via ODBC. " +
                      "Please ensure 'Microsoft Access Database Engine' (ODBC driver) is installed on this system, " +
                      "or export the legacy tables to CSV format for direct import.";
            _logger.LogError(lastEx, "{Message}", msg);
            errors.Add(msg);
            if (lastEx != null) errors.Add($"Driver details: {lastEx.Message}");
            return new LegacyImportResult(0, 0, 0, 0, errors.Count, errors);
        }

        using (connection)
        {
            try
            {
                var tablesSchema = connection.GetSchema("Tables");
                var tableNames = new List<string>();
                foreach (DataRow row in tablesSchema.Rows)
                {
                    var tableType = row["TABLE_TYPE"]?.ToString();
                    if (string.Equals(tableType, "TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = row["TABLE_NAME"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            tableNames.Add(name);
                        }
                    }
                }

                progress?.Report(0.1);

                // 1. Import Parties
                var partyTable = tableNames.FirstOrDefault(t =>
                    t.Contains("party", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("customer", StringComparison.OrdinalIgnoreCase));

                if (partyTable != null)
                {
                    partiesCount = await ImportPartiesFromTableAsync(connection, partyTable, errors, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(0.3);

                // 2. Import Materials
                var materialTable = tableNames.FirstOrDefault(t =>
                    t.Contains("material", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("item", StringComparison.OrdinalIgnoreCase));

                if (materialTable != null)
                {
                    materialsCount = await ImportMaterialsFromTableAsync(connection, materialTable, errors, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(0.5);

                // 3. Import Vehicles
                var vehicleTable = tableNames.FirstOrDefault(t =>
                    t.Contains("vehicle", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("truck", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("lorry", StringComparison.OrdinalIgnoreCase));

                if (vehicleTable != null)
                {
                    vehiclesCount = await ImportVehiclesFromTableAsync(connection, vehicleTable, errors, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(0.7);

                // 4. Import Weighments
                var weighmentTable = tableNames.FirstOrDefault(t =>
                    t.Contains("weighment", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("ticket", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("slip", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("weight", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("trans", StringComparison.OrdinalIgnoreCase));

                if (weighmentTable != null)
                {
                    weighmentsCount = await ImportWeighmentsFromTableAsync(connection, weighmentTable, errors, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(1.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading MDB tables: {Message}", ex.Message);
                errors.Add($"Error during MDB read: {ex.Message}");
            }
        }

        return new LegacyImportResult(partiesCount, materialsCount, vehiclesCount, weighmentsCount, errors.Count, errors);
    }

    private async Task<int> ImportPartiesFromTableAsync(OdbcConnection connection, string tableName, List<string> errors, CancellationToken cancellationToken)
    {
        int count = 0;
        try
        {
            using var cmd = new OdbcCommand($"SELECT * FROM [{tableName}]", connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using var uow = _unitOfWork();
            var repo = uow.Repository<Party>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = GetColumnString(reader, "PartyName", "Party_Name", "Name", "CustomerName", "Customer");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var code = GetColumnString(reader, "PartyCode", "Party_Code", "Code");
                var address = GetColumnString(reader, "Address", "Address1", "Addr");
                var phone = GetColumnString(reader, "Phone", "Mobile", "Contact", "Tel");
                var email = GetColumnString(reader, "Email", "Mail");

                try
                {
                    var party = Party.Create(name.Trim(), code?.Trim(), address?.Trim(), phone?.Trim(), email?.Trim());
                    await repo.AddAsync(party, cancellationToken).ConfigureAwait(false);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Party '{name}': {ex.Message}");
                }
            }

            if (count > 0)
            {
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Table {tableName}: {ex.Message}");
        }

        return count;
    }

    private async Task<int> ImportMaterialsFromTableAsync(OdbcConnection connection, string tableName, List<string> errors, CancellationToken cancellationToken)
    {
        int count = 0;
        try
        {
            using var cmd = new OdbcCommand($"SELECT * FROM [{tableName}]", connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using var uow = _unitOfWork();
            var repo = uow.Repository<Material>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = GetColumnString(reader, "MaterialName", "Material_Name", "Name", "ProductName", "Product", "ItemName", "Item");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var code = GetColumnString(reader, "MaterialCode", "Material_Code", "Code");
                var desc = GetColumnString(reader, "Description", "Desc");

                try
                {
                    var mat = Material.Create(name.Trim(), code?.Trim(), desc?.Trim());
                    await repo.AddAsync(mat, cancellationToken).ConfigureAwait(false);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Material '{name}': {ex.Message}");
                }
            }

            if (count > 0)
            {
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Table {tableName}: {ex.Message}");
        }

        return count;
    }

    private async Task<int> ImportVehiclesFromTableAsync(OdbcConnection connection, string tableName, List<string> errors, CancellationToken cancellationToken)
    {
        int count = 0;
        try
        {
            using var cmd = new OdbcCommand($"SELECT * FROM [{tableName}]", connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using var uow = _unitOfWork();
            var repo = uow.Repository<Vehicle>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var number = GetColumnString(reader, "VehicleNo", "Vehicle_No", "VehicleNumber", "TruckNo", "RegNo");
                if (string.IsNullOrWhiteSpace(number)) continue;

                var tare = GetColumnDecimal(reader, "Tare", "TareWeight", "Tare_Weight");

                try
                {
                    var veh = Vehicle.Create(number.Trim(), tareWeightKg: tare);
                    await repo.AddAsync(veh, cancellationToken).ConfigureAwait(false);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Vehicle '{number}': {ex.Message}");
                }
            }

            if (count > 0)
            {
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Table {tableName}: {ex.Message}");
        }

        return count;
    }

    private async Task<int> ImportWeighmentsFromTableAsync(OdbcConnection connection, string tableName, List<string> errors, CancellationToken cancellationToken)
    {
        int count = 0;
        try
        {
            using var cmd = new OdbcCommand($"SELECT * FROM [{tableName}]", connection);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using var uow = _unitOfWork();
            var repo = uow.Repository<Weighment>();

            var batch = new List<Weighment>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var vehicleNo = GetColumnString(reader, "VehicleNo", "Vehicle_No", "VehicleNumber", "TruckNo");
                if (string.IsNullOrWhiteSpace(vehicleNo)) continue;

                var party = GetColumnString(reader, "PartyName", "Party", "Customer");
                var material = GetColumnString(reader, "MaterialName", "Material", "Product");
                var gross = GetColumnDecimal(reader, "Gross", "GrossWeight", "Gross_Weight") ?? 0m;
                var tare = GetColumnDecimal(reader, "Tare", "TareWeight", "Tare_Weight") ?? 0m;
                var charges = GetColumnDecimal(reader, "Charges", "Amount", "Fee") ?? 0m;
                var remarks = GetColumnString(reader, "Remarks", "Remark", "Notes");

                try
                {
                    var wm = Weighment.Open(
                        vehicleNo.Trim(),
                        WeighmentMode.GrossFirst,
                        partyName: party?.Trim(),
                        materialName: material?.Trim(),
                        charges: charges,
                        remarks: remarks);

                    if (gross > 0m)
                    {
                        wm.RecordFirstWeight(new WeightCapture(gross, DateTime.UtcNow, WeightSource.Manual));
                    }

                    if (tare > 0m && gross > 0m)
                    {
                        wm.RecordSecondWeight(new WeightCapture(tare, DateTime.UtcNow, WeightSource.Manual), NetWeightPolicy.AllowZero);
                    }

                    await repo.AddAsync(wm, cancellationToken).ConfigureAwait(false);
                    batch.Add(wm);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Weighment '{vehicleNo}': {ex.Message}");
                }
            }

            if (batch.Count > 0)
            {
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in batch)
                {
                    if (item.Id > 0 && string.IsNullOrEmpty(item.SlipNumber))
                    {
                        item.AssignSlipNumber();
                    }
                }
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Table {tableName}: {ex.Message}");
        }

        return count;
    }

    private async Task<LegacyImportResult> ImportCsvAsync(
        string filePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        int count = 0;

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (lines.Length <= 1)
            {
                return new LegacyImportResult(0, 0, 0, 0, 0, []);
            }

            var header = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
            var vehIdx = FindIndex(header, "VehicleNo", "Vehicle_No", "VehicleNumber", "TruckNo");
            var partyIdx = FindIndex(header, "PartyName", "Party", "Customer");
            var matIdx = FindIndex(header, "MaterialName", "Material", "Product");
            var grossIdx = FindIndex(header, "Gross", "GrossWeight", "Gross_Weight");
            var tareIdx = FindIndex(header, "Tare", "TareWeight", "Tare_Weight");
            var chargesIdx = FindIndex(header, "Charges", "Amount", "Fee");
            var remarksIdx = FindIndex(header, "Remarks", "Remark", "Notes");

            await using var uow = _unitOfWork();
            var repo = uow.Repository<Weighment>();

            var batch = new List<Weighment>();
            for (int i = 1; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
                if (vehIdx < 0 || vehIdx >= cols.Length) continue;

                var vehicleNo = cols[vehIdx];
                if (string.IsNullOrWhiteSpace(vehicleNo)) continue;

                var party = partyIdx >= 0 && partyIdx < cols.Length ? cols[partyIdx] : null;
                var material = matIdx >= 0 && matIdx < cols.Length ? cols[matIdx] : null;
                var gross = grossIdx >= 0 && grossIdx < cols.Length && decimal.TryParse(cols[grossIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var g) ? g : 0m;
                var tare = tareIdx >= 0 && tareIdx < cols.Length && decimal.TryParse(cols[tareIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : 0m;
                var charges = chargesIdx >= 0 && chargesIdx < cols.Length && decimal.TryParse(cols[chargesIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : 0m;
                var remarks = remarksIdx >= 0 && remarksIdx < cols.Length ? cols[remarksIdx] : null;

                try
                {
                    var wm = Weighment.Open(
                        vehicleNo.Trim(),
                        WeighmentMode.GrossFirst,
                        partyName: party?.Trim(),
                        materialName: material?.Trim(),
                        charges: charges,
                        remarks: remarks);

                    if (gross > 0m)
                    {
                        wm.RecordFirstWeight(new WeightCapture(gross, DateTime.UtcNow, WeightSource.Manual));
                    }

                    if (tare > 0m && gross > 0m)
                    {
                        wm.RecordSecondWeight(new WeightCapture(tare, DateTime.UtcNow, WeightSource.Manual), NetWeightPolicy.AllowZero);
                    }

                    await repo.AddAsync(wm, cancellationToken).ConfigureAwait(false);
                    batch.Add(wm);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Line {i + 1} ({vehicleNo}): {ex.Message}");
                }

                if (i % 50 == 0)
                {
                    progress?.Report((double)i / lines.Length);
                }
            }

            if (batch.Count > 0)
            {
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in batch)
                {
                    if (item.Id > 0 && string.IsNullOrEmpty(item.SlipNumber))
                    {
                        item.AssignSlipNumber();
                    }
                }
                await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(1.0);
        }
        catch (Exception ex)
        {
            errors.Add($"CSV read error: {ex.Message}");
        }

        return new LegacyImportResult(0, 0, 0, count, errors.Count, errors);
    }

    private static string? GetColumnString(IDataReader reader, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!reader.IsDBNull(i))
                    {
                        var val = reader.GetValue(i)?.ToString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
        }
        return null;
    }

    private static decimal? GetColumnDecimal(IDataReader reader, params string[] candidateNames)
    {
        var str = GetColumnString(reader, candidateNames);
        if (!string.IsNullOrWhiteSpace(str) && decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            return val;
        }
        return null;
    }

    private static int FindIndex(string[] array, params string[] candidates)
    {
        for (int i = 0; i < array.Length; i++)
        {
            foreach (var cand in candidates)
            {
                if (string.Equals(array[i], cand, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return -1;
    }
}
