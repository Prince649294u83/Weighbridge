using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Security;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Reporting.Services;
using WeighBridge.Tests.Infrastructure;
using WeighBridge.Tests.Weighments;

namespace WeighBridge.Tests.Reporting;

/// <summary>
/// Report dispatch, row caps, spreadsheet-injection neutralisation and input sanity â€” the
/// hardening that used to be missing: every report key produced the same dump, the row
/// limit from configuration was ignored, and a party named <c>=SUM(A1)</c> would execute
/// as a formula the moment the CSV was opened in Excel.
/// </summary>
public sealed class CsvReportHardeningTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();
    private readonly TempDataRoot _temp = new();

    public CsvReportHardeningTests()
    {
        _harness.SignInAs(Roles.Administrator);
    }

    public void Dispose()
    {
        _harness.Dispose();
        _temp.Dispose();
    }

    private CsvReportService CreateService(int maxRows = 100_000) => new(
        () => new UnitOfWork(_harness.CreateContext()),
        _harness.Permissions,
        Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory,
            MaxRowsPerReport = maxRows,
        }),
        NullLogger<CsvReportService>.Instance);

    private static Dictionary<string, object?> Params(string start = "-1", string end = "1") => new()
    {
        ["StartDate"] = DateTime.Today.AddDays(int.Parse(start)),
        ["EndDate"] = DateTime.Today.AddDays(int.Parse(end)),
    };

    [Fact]
    public async Task PartyWeighments_OnlyIncludeTheRequestedParty()
    {
        await Complete("MH12AA0001", "Alpha Corp", "Coal");
        await Complete("MH12AA0002", "Beta Corp", "Sand");

        var result = await CreateService().GenerateAsync(
            "PartyWeighments",
            new Dictionary<string, object?>
            {
                ["StartDate"] = DateTime.Today.AddDays(-1),
                ["EndDate"] = DateTime.Today.AddDays(1),
                ["PartyName"] = "Alpha Corp",
            },
            ReportFormat.Csv);

        Assert.True(result.Succeeded, result.Message);
        var lines = await File.ReadAllLinesAsync(result.OutputPath!);

        var body = lines.Skip(1).Where(l => l.Length > 0).ToArray();
        Assert.Single(body);
        Assert.Contains("Alpha Corp", body[0]);
        Assert.DoesNotContain("Beta Corp", string.Join('\n', body));
    }

    [Fact]
    public async Task MaterialWeighments_OnlyIncludeTheRequestedMaterial()
    {
        await Complete("MH12AA0011", "Alpha Corp", "Coal");
        await Complete("MH12AA0012", "Alpha Corp", "Sand");

        var result = await CreateService().GenerateAsync(
            "MaterialWeighments",
            new Dictionary<string, object?>
            {
                ["StartDate"] = DateTime.Today.AddDays(-1),
                ["EndDate"] = DateTime.Today.AddDays(1),
                ["MaterialName"] = "Coal",
            },
            ReportFormat.Csv);

        Assert.True(result.Succeeded, result.Message);
        var body = (await File.ReadAllLinesAsync(result.OutputPath!)).Skip(1).Where(l => l.Length > 0).ToArray();
        Assert.Single(body);
        Assert.Contains("Coal", body[0]);
    }

    [Fact]
    public async Task DailyWeighments_IgnoreNameFilters_AndRejectUnknownKeys()
    {
        await Complete("MH12AA0021", "Alpha Corp", "Coal");
        await Complete("MH12AA0022", "Beta Corp", "Sand");

        // The daily report is deliberately unfiltered: it is the whole day.
        var daily = await CreateService().GenerateAsync("DailyWeighments", Params(), ReportFormat.Csv);
        Assert.True(daily.Succeeded, daily.Message);
        var body = (await File.ReadAllLinesAsync(daily.OutputPath!)).Skip(1).Where(l => l.Length > 0).ToArray();
        Assert.Equal(2, body.Length);

        var unknown = await CreateService().GenerateAsync(
            "QuarterlyProfit",
            Params(),
            ReportFormat.Csv);
        Assert.False(unknown.Succeeded);
        Assert.Contains("Unknown report", unknown.Message);
    }

    [Fact]
    public async Task MaxRowsPerReport_TruncatesAndSaysSo()
    {
        for (var i = 0; i < 7; i++)
        {
            await Complete($"MH12AA03{i:D2}", "Cap Corp", "Gravel");
        }

        var service = CreateService(maxRows: 3);
        var result = await service.GenerateAsync("DailyWeighments", Params(), ReportFormat.Csv);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("row limit", result.Message);

        var body = (await File.ReadAllLinesAsync(result.OutputPath!)).Skip(1).Where(l => l.Length > 0).ToArray();
        Assert.Equal(3, body.Length);
    }

    [Fact]
    public async Task EndDateBeforeStartDate_IsRefused()
    {
        var result = await CreateService().GenerateAsync("DailyWeighments", Params(start: "5", end: "-5"), ReportFormat.Csv);

        Assert.False(result.Succeeded);
        Assert.Null(result.OutputPath);
    }

    /// <summary>A value whose first character would execute in Excel arrives as text.</summary>
    [Fact]
    public async Task FormulaPrefixes_AreNeutralised()
    {
        await Complete("MH12AA0031", "=SUM(A1:A9)", "+CMD");
        var result = await CreateService().GenerateAsync("DailyWeighments", Params(), ReportFormat.Csv);

        Assert.True(result.Succeeded, result.Message);
        var text = await File.ReadAllTextAsync(result.OutputPath!);

        Assert.Contains("'=SUM(A1:A9)", text);
        Assert.Contains("'+CMD", text);
    }

    private async Task Complete(string vehicle, string party, string material)
    {
        var created = await _harness.Service.CreateAsync(new Core.Abstractions.NewWeighment
        {
            VehicleNumber = vehicle,
            Mode = WeighmentMode.GrossFirst,
            PartyName = party,
            MaterialName = material,
        });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 20_000m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(created.Id, 10_000m, WeightSource.Manual);
    }
}
