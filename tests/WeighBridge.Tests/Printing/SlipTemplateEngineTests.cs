using System.Text;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Template;
using WeighBridge.Printing.Template.Ast;
using Xunit;

namespace WeighBridge.Tests.Printing;

public sealed class SlipTemplateEngineTests
{
    private readonly SlipTemplateEngine _engine = new();

    private static Weighment CreateSampleCompletedWeighment()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH12AB1234",
            mode: WeighmentMode.GrossFirst,
            partyName: "Tata Steel Ltd",
            materialName: "Iron Ore",
            driverName: "Ramesh Kumar",
            transporterName: "National Logistics",
            remarks: "Standard Dispatch",
            charges: 150.00m,
            numberOfBags: 50,
            bagWeightKg: 0.50m,
            gatePassNumber: "GP-98765",
            customField1: "Consigner Sub-Unit 4",
            customField2: "Seal-12345");

        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 42L);
        weighment.AssignSlipNumber();

        // 1st Weight (Gross)
        weighment.RecordFirstWeight(new WeightCapture(35420.0m, DateTime.UtcNow, WeightSource.Indicator));

        // Update second entry details
        weighment.UpdateSecondEntryDetails(
            secondCharges: 50.00m,
            numberOfBags: 50,
            bagWeightKg: 0.50m,
            gatePassNumber: "GP-98765",
            remarks: "Standard Dispatch",
            customField3: "Gate-2",
            customField4: "Checked");

        // 2nd Weight (Tare)
        weighment.RecordSecondWeight(new WeightCapture(12340.0m, DateTime.UtcNow, WeightSource.Indicator));

        return weighment;
    }

    private static CompanyOptions CreateSampleCompanyOptions()
    {
        return new CompanyOptions
        {
            CompanyName = "METRO WEIGHBRIDGE SERVICES",
            AddressLine1 = "Plot 42, Industrial Area Phase 1",
            AddressLine2 = "Pune, Maharashtra - 411018",
            Phone = "+91 20 12345678",
            Email = "contact@metroweigh.com",
            TaxId = "27AAAAA0000A1Z5"
        };
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("advanced")]
    [InlineData("fci")]
    [InlineData("thermal")]
    [InlineData("dot")]
    [InlineData("a4")]
    public void Parse_AllSixLegacyFormats_SucceedsWithoutErrors(string templateName)
    {
        string templateContent = BuiltInTemplates.GetByName(templateName);
        var document = _engine.Parse(templateContent, strictValidation: false);
        Assert.NotNull(document);
        Assert.NotEmpty(document.Nodes);
    }

    [Fact]
    public void Validate_UnknownToken_ReturnsUnknownTokenError()
    {
        string malformed = "Slip No: <ticket> Weight: <gweigth>"; // typo in gweight
        var result = _engine.Validate(malformed);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == TemplateErrorCategory.UnknownToken && e.Message.Contains("gweigth"));
    }

    [Fact]
    public void Validate_UnclosedTag_ReturnsUnclosedTokenError()
    {
        string unclosed = "Slip: <ticket Party: <party>";
        var result = _engine.Validate(unclosed);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == TemplateErrorCategory.UnclosedToken);
    }

    [Fact]
    public void Validate_CapabilityMismatch_FlagsCutOnGdiProfile()
    {
        string thermalTemplate = "<Start><SlipType><Cut>";
        var gdiProfile = PrinterProfile.DefaultGdi("Canon Laser");

        var result = _engine.Validate(thermalTemplate, gdiProfile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Category == TemplateErrorCategory.CapabilityMismatch && e.Message.Contains("<Cut>"));
    }

    [Fact]
    public void Parse_PreParseLimits_ExceedingSize_ThrowsLimitExceeded()
    {
        string hugeTemplate = new string('A', SlipTemplateEngine.MaxTemplateBytes + 10);

        var ex = Assert.Throws<TemplateParseException>(() => _engine.Parse(hugeTemplate));
        Assert.Contains(ex.Errors, e => e.Category == TemplateErrorCategory.LimitExceeded);
    }

    [Fact]
    public void RenderToText_ResolvesCanonicalTokens_AndCalculationFreeNet()
    {
        var weighment = CreateSampleCompletedWeighment();
        var company = CreateSampleCompanyOptions();
        var printData = WeighmentPrintDataFactory.Create(weighment, company, operatorDisplayName: "SuperOperator");

        var document = _engine.Parse(BuiltInTemplates.Advanced);
        var profile = PrinterProfile.DefaultGdi("Office Printer");

        string rendered = _engine.RenderToText(document, printData, profile);

        // Assert domain net weight is used directly (35420.0 - 12340.0 = 23080.0)
        Assert.Contains("23080.0 kg", rendered);
        // Assert bags deduction (50 bags * 0.50 kg = 25.00 kg)
        Assert.Contains("25.00 kg", rendered);
        // Assert actual weight (23080.0 - 25.00 = 23055.0 kg)
        Assert.Contains("23055.0 kg", rendered);
        // Assert gate pass
        Assert.Contains("GP-98765", rendered);
        // Assert total charges (150.00 + 50.00 = 200.00)
        Assert.Contains("200.00", rendered);
        // Assert company header
        Assert.Contains("METRO WEIGHBRIDGE SERVICES", rendered);
        // Assert vehicle registration
        Assert.Contains("MH12AB1234", rendered);
        // Assert operator
        Assert.Contains("SuperOperator", rendered);
    }

    [Fact]
    public void RenderToText_FciFormat_ResolvesConsignerAndGrain()
    {
        var weighment = CreateSampleCompletedWeighment();
        var company = CreateSampleCompanyOptions();
        var printData = WeighmentPrintDataFactory.Create(weighment, company);

        var document = _engine.Parse(BuiltInTemplates.FCI);
        var profile = PrinterProfile.DefaultGdi("Office Printer");

        string rendered = _engine.RenderToText(document, printData, profile);

        // <field1> resolved to CustomField1 (Consigner Sub-Unit 4)
        Assert.Contains("Consigner Sub-Unit 4", rendered);
        // <item> resolved to Grain/Material (Iron Ore)
        Assert.Contains("Iron Ore", rendered);
        // <field2> resolved to Bags (50)
        Assert.Contains("50", rendered);
        // <field4> resolved to Actual Wt (23055.0 kg)
        Assert.Contains("23055.0 kg", rendered);
    }

    [Fact]
    public void RenderToBytes_Thermal_EmitsEscPosCutAndBoldBytes()
    {
        var weighment = CreateSampleCompletedWeighment();
        var printData = WeighmentPrintDataFactory.Create(weighment);

        var document = _engine.Parse(BuiltInTemplates.Thermal);
        var profile = PrinterProfile.Thermal80mm("Epson POS");

        byte[] rawBytes = _engine.RenderToBytes(document, printData, profile);

        // Check for ESC @ (0x1B, 0x40) Start directive
        Assert.Equal(0x1B, rawBytes[0]);
        Assert.Equal(0x40, rawBytes[1]);

        // Check for GS V 'B' 0 (0x1D, 0x56, 0x42, 0x00) Cut directive at the end
        Assert.True(rawBytes.Length >= 4);
        int lastIdx = rawBytes.Length - 4;
        Assert.Equal(0x1D, rawBytes[lastIdx]);
        Assert.Equal(0x56, rawBytes[lastIdx + 1]);
        Assert.Equal(0x42, rawBytes[lastIdx + 2]);
        Assert.Equal(0x00, rawBytes[lastIdx + 3]);
    }

    [Fact]
    public void RenderToText_UnicodeIndianLanguage_RendersAccurately()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH14GH9999",
            mode: WeighmentMode.GrossFirst,
            partyName: "श्री गणेश ट्रेडर्स",
            materialName: "सोयाबीन धान्य");

        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 88L);
        weighment.AssignSlipNumber();
        weighment.RecordFirstWeight(new WeightCapture(20000.0m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(8000.0m, DateTime.UtcNow, WeightSource.Indicator));

        var printData = WeighmentPrintDataFactory.Create(weighment);
        var document = _engine.Parse(BuiltInTemplates.Standard);
        var profile = PrinterProfile.DefaultGdi("GDI Printer");

        string rendered = _engine.RenderToText(document, printData, profile);

        Assert.Contains("श्री गणेश ट्रेडर्स", rendered);
        Assert.Contains("सोयाबीन धान्य", rendered);
    }
}
