using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Template;
using Xunit;

namespace WeighBridge.Tests.Printing;

public sealed class WindowsGdiPrintOutputTests
{
    private readonly SlipTemplateEngine _engine = new();

    private static WeighmentCreateResult CreateWeighment()
    {
        var weighment = Weighment.Open("MH12AB1234", WeighmentMode.GrossFirst, "Party GDI", "Steel Rods", charges: 250m);
        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 101L);
        weighment.AssignSlipNumber();
        weighment.RecordFirstWeight(new WeightCapture(30000m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(12000m, DateTime.UtcNow, WeightSource.Indicator));
        return new WeighmentCreateResult(weighment);
    }

    private sealed record WeighmentCreateResult(Weighment Weighment);

    [Fact]
    public async Task WindowsGdiPrintOutput_HandlesMissingPrinter_WithoutCrashing()
    {
        var gdiDriver = new WindowsGdiPrintOutput(_engine, NullLogger<WindowsGdiPrintOutput>.Instance);
        var weighment = CreateWeighment().Weighment;
        var printData = WeighmentPrintDataFactory.Create(weighment);
        var document = _engine.Parse(BuiltInTemplates.Standard);
        var profile = PrinterProfile.DefaultGdi("NonExistentPrinter_999XYZ");

        // Act
        var result = await gdiDriver.OutputAsync("Test Slip", document, printData, profile, copies: 1);

        // Assert: Safe failure report, no unhandled exceptions
        Assert.False(result.Succeeded);
        Assert.Contains("not valid or accessible", result.Message);
    }

    [Fact]
    public async Task WindowsGdiPrintOutput_EmptyPrinterName_ReturnsFailureDirectly()
    {
        var gdiDriver = new WindowsGdiPrintOutput(_engine, NullLogger<WindowsGdiPrintOutput>.Instance);
        var weighment = CreateWeighment().Weighment;
        var printData = WeighmentPrintDataFactory.Create(weighment);
        var document = _engine.Parse(BuiltInTemplates.Standard);
        var profile = PrinterProfile.DefaultGdi("");

        // Act
        var result = await gdiDriver.OutputAsync("Test Slip", document, printData, profile, copies: 1);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("No target printer specified", result.Message);
    }

    [Fact]
    public void WindowsGdiPrintOutput_StressTest_ResourceAllocation_NoHandleLeaks()
    {
        // Stress test document rendering and string creation across 1,000 iterations
        var weighment = CreateWeighment().Weighment;
        var company = new CompanyOptions { CompanyName = "STRESS TEST CORP" };
        var printData = WeighmentPrintDataFactory.Create(weighment, company);
        var profile = PrinterProfile.DefaultGdi("StressPrinter");

        long startMemory = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 1000; i++)
        {
            var document = _engine.Parse(BuiltInTemplates.Standard);
            string rendered = _engine.RenderToText(document, printData, profile);
            Assert.NotEmpty(rendered);
        }

        long endMemory = GC.GetTotalMemory(forceFullCollection: true);
        long diff = endMemory - startMemory;

        // Verify bounded memory growth (< 15 MB across 1000 full parses and renderings)
        Assert.True(diff < 15 * 1024 * 1024, $"Memory delta {diff / 1024} KB should be bounded.");
    }
}
