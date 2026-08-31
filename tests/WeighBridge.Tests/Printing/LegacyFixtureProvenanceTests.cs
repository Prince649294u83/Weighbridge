using System.Security.Cryptography;
using Xunit;

namespace WeighBridge.Tests.Printing;

/// <summary>
/// Verifies the cryptographic SHA-256 provenance of all byte-preserving legacy baseline fixtures.
/// </summary>
public sealed class LegacyFixtureProvenanceTests
{
    private static readonly Dictionary<string, string> ExpectedProvenanceHashes = new()
    {
        ["Fixtures/Legacy/Printing/Print_Ticket.txt"] = "9198883452C941AD069948EC8424FB43DB03D1B7C4201E6C7C0FBBCC8B73A93C",
        ["Fixtures/Legacy/Printing/Print_Ticket_Advanced.txt"] = "19C710C64A766577808E4E4D157FCFBF742F225439E77C116A2A1721AEFE4B1A",
        ["Fixtures/Legacy/Printing/Print_Ticket_FCI.txt"] = "A8545DF1409700814D97F9DC862C3789B1A59A9B9EC23FC9DB1C7EF10D5B441F",
        ["Fixtures/Legacy/Printing/Print_Ticket_Thermal.txt"] = "2F2ADE54AD52BC545B3A65BF86AF057BD36901E387071BAFC7413D6B77365C69",
        ["Fixtures/Legacy/Printing/Print_Ticket_Dot.txt"] = "E69882C138B741F2BD51C9BC0EE206247A6F2893950BE65346034EDB3D8200BA",
        ["Fixtures/Legacy/Printing/Print_Ticket_A4.txt"] = "555359DB3EB6AF3287B7CE82B9D3E210417E1BB291391F7167339D68501CA5F1",
        ["Fixtures/Legacy/Messaging/SMS Format.txt"] = "76A70566AA73312DB37D9CE975C444C55C9EEA2D82965EB760AFB5A15A0C1FF9"
    };

    [Theory]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket.txt")]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket_Advanced.txt")]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket_FCI.txt")]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket_Thermal.txt")]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket_Dot.txt")]
    [InlineData("Fixtures/Legacy/Printing/Print_Ticket_A4.txt")]
    [InlineData("Fixtures/Legacy/Messaging/SMS Format.txt")]
    public void LegacyFixture_Sha256Hash_MatchesRecordedProvenance(string relativePath)
    {
        // Arrange
        var baseDir = AppContext.BaseDirectory;
        var filePath = Path.Combine(baseDir, relativePath);

        // If running directly from test root without copy-to-output:
        if (!File.Exists(filePath))
        {
            var projectDir = Path.GetFullPath(Path.Combine(baseDir, "../../../"));
            filePath = Path.Combine(projectDir, relativePath);
        }

        Assert.True(File.Exists(filePath), $"Fixture file must exist at path: {filePath}");

        // Act
        byte[] fileBytes = File.ReadAllBytes(filePath);
        byte[] hashBytes = SHA256.HashData(fileBytes);
        string actualHash = Convert.ToHexString(hashBytes);

        // Assert
        string expectedHash = ExpectedProvenanceHashes[relativePath];
        Assert.Equal(expectedHash, actualHash);
    }
}
