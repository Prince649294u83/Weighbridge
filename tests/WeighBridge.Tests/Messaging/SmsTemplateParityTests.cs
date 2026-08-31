using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Messaging;
using Xunit;

namespace WeighBridge.Tests.Messaging;

public sealed class SmsTemplateParityTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Legacy", "Messaging", "SMS Format.txt");

    private readonly SmsTemplateEngine _engine = new();

    private static Weighment CreateSampleCompletedWeighment()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH12AB1234",
            mode: WeighmentMode.GrossFirst,
            partyName: "Tata Steel Ltd",
            materialName: "Iron Ore",
            charges: 150.00m);

        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 42L);
        weighment.AssignSlipNumber();

        // Gross: 35420.0 kg
        weighment.RecordFirstWeight(new WeightCapture(35420.0m, DateTime.UtcNow, WeightSource.Indicator));

        // Tare: 12340.0 kg, Net: 23080.0 kg, Second Charges: 50.00
        weighment.UpdateSecondEntryDetails(
            secondCharges: 50.00m,
            numberOfBags: null,
            bagWeightKg: null,
            gatePassNumber: null,
            remarks: null,
            customField3: null,
            customField4: null);
        weighment.RecordSecondWeight(new WeightCapture(12340.0m, DateTime.UtcNow, WeightSource.Indicator));

        return weighment;
    }

    [Fact]
    public void SmsTemplateEngine_RendersAllNineFields_FromLegacyFixtureFormat()
    {
        string fixtureTemplate = File.ReadAllText(FixturePath);
        var weighment = CreateSampleCompletedWeighment();
        var company = new CompanyOptions { CompanyName = "Metro Weigh" };
        var printData = WeighmentPrintDataFactory.Create(weighment, company);

        string rendered = _engine.Render(fixtureTemplate, printData);

        // 1. Ticket No
        Assert.Contains($"Ticket No {printData.SlipNumber}", rendered);
        // 2. Vehicle No
        Assert.Contains("Vehicle No MH12AB1234", rendered);
        // 3. Party
        Assert.Contains("Party Tata Steel Ltd", rendered);
        // 4. Item
        Assert.Contains("Item Iron Ore", rendered);
        // 5. Charges (150.00 + 50.00 = 200.00)
        Assert.Contains("Charges 200.00", rendered);
        // 6. VehicalType
        Assert.Contains($"VehicalType {printData.VehicleTypeName}", rendered);
        // 7. G WT
        Assert.Contains("G WT35420.0", rendered);
        // 8. T WT
        Assert.Contains("T WT 12340.0", rendered);
        // 9. Net WT
        Assert.Contains("Net WT 23080.0", rendered);
    }
}
