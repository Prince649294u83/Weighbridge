using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Reporting.Services;
using WeighBridge.Tests.Infrastructure;
using WeighBridge.Tests.Weighments;

namespace WeighBridge.Tests.Reporting;

public sealed class CsvReportServiceTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();
    private readonly TempDataRoot _temp = new();

    public CsvReportServiceTests()
    {
        _harness.SignInAs(Core.Security.Roles.Administrator);
    }

    public void Dispose()
    {
        _harness.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task GenerateDailyReportAsync_CreatesValidCsvFile()
    {
        // Arrange
        var w1 = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB1001",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Acme Corp",
            MaterialName = "Coal"
        });
        await _harness.Service.RecordFirstWeightAsync(w1.Id, 25000m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(w1.Id, 10000m, WeightSource.Indicator);

        var unitOfWorkFactory = () => new UnitOfWork(_harness.CreateContext());
        var options = Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory
        });

        var service = new CsvReportService(unitOfWorkFactory, _harness.Permissions, options, NullLogger<CsvReportService>.Instance);

        var parameters = new Dictionary<string, object?>
        {
            ["StartDate"] = DateTime.Today.AddDays(-1),
            ["EndDate"] = DateTime.Today.AddDays(1)
        };

        // Act
        var result = await service.GenerateAsync("DailyWeighments", parameters, ReportFormat.Csv);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        var lines = await File.ReadAllLinesAsync(result.OutputPath);
        Assert.True(lines.Length >= 2);
        Assert.Contains("MH12AB1001", lines[1]);
        Assert.Contains("Acme Corp", lines[1]);
        Assert.Contains("Coal", lines[1]);
    }

    [Fact]
    public async Task GenerateMaterialSummaryReportAsync_FiltersCorrectly()
    {
        // Arrange
        var w1 = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB2001",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Alpha Corp",
            MaterialName = "Iron Ore"
        });
        await _harness.Service.RecordFirstWeightAsync(w1.Id, 30000m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(w1.Id, 12000m, WeightSource.Indicator);

        var unitOfWorkFactory = () => new UnitOfWork(_harness.CreateContext());
        var options = Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory
        });

        var service = new CsvReportService(unitOfWorkFactory, _harness.Permissions, options, NullLogger<CsvReportService>.Instance);

        var parameters = new Dictionary<string, object?>
        {
            ["StartDate"] = DateTime.Today.AddDays(-1),
            ["EndDate"] = DateTime.Today.AddDays(1),
            ["MaterialName"] = "Iron Ore"
        };

        // Act
        var result = await service.GenerateAsync("MaterialWeighments", parameters, ReportFormat.Csv);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        var lines = await File.ReadAllLinesAsync(result.OutputPath);
        Assert.True(lines.Length >= 2);
        Assert.Contains("Iron Ore", lines[1]);
    }

    /// <summary>
    /// Reports.Export is withheld from Operator and ReadOnly, and was enforced nowhere - so
    /// every signed-in account could write the site's whole weighment history to a file and
    /// take it away. Enforced in the service rather than in front of the one view model that
    /// calls it, because this is the method that leaves the file on disk.
    /// </summary>
    [Theory]
    [InlineData("Operator")]
    [InlineData("ReadOnly")]
    public async Task GenerateAsync_WithoutTheExportPermission_RefusesAndWritesNothing(string roleName)
    {
        var w1 = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB3001",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Bravo Corp",
            MaterialName = "Sand"
        });
        await _harness.Service.RecordFirstWeightAsync(w1.Id, 28000m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(w1.Id, 11000m, WeightSource.Indicator);

        var unitOfWorkFactory = () => new UnitOfWork(_harness.CreateContext());
        var options = Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory
        });

        var service = new CsvReportService(unitOfWorkFactory, _harness.Permissions, options, NullLogger<CsvReportService>.Instance);

        _harness.SignInAs(Core.Security.Roles.FromName(roleName)!);

        var result = await service.GenerateAsync(
            "DailyWeighments",
            new Dictionary<string, object?>
            {
                ["StartDate"] = DateTime.Today.AddDays(-1),
                ["EndDate"] = DateTime.Today.AddDays(1)
            },
            ReportFormat.Csv);

        Assert.False(result.Succeeded);
        Assert.Null(result.OutputPath);
        Assert.Contains("export reports", result.Message, StringComparison.OrdinalIgnoreCase);

        // The refusal has to happen before the writer opens, not after. The directory is
        // created by GenerateAsync, so on a clean refusal it does not exist at all.
        Assert.Empty(Directory.Exists(_temp.Paths.ReportsDirectory)
            ? Directory.GetFiles(_temp.Paths.ReportsDirectory, "*.csv")
            : []);
    }

    /// <summary>
    /// The counterpart: a role that does hold the permission still gets its report, or the
    /// check above would be indistinguishable from the feature being broken.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_AsSupervisor_StillProducesTheReport()
    {
        var unitOfWorkFactory = () => new UnitOfWork(_harness.CreateContext());
        var options = Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory
        });

        var service = new CsvReportService(unitOfWorkFactory, _harness.Permissions, options, NullLogger<CsvReportService>.Instance);

        _harness.SignInAs(Core.Security.Roles.Supervisor);

        var result = await service.GenerateAsync(
            "DailyWeighments",
            new Dictionary<string, object?>
            {
                ["StartDate"] = DateTime.Today.AddDays(-1),
                ["EndDate"] = DateTime.Today.AddDays(1)
            },
            ReportFormat.Csv);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(result.OutputPath));
    }
}
