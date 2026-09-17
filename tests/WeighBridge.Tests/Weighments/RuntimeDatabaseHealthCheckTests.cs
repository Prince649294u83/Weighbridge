using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace WeighBridge.Tests.Weighments;

public sealed class RuntimeDatabaseHealthCheckTests(ITestOutputHelper output)
{
    [Fact]
    public void Runtime_Database_Can_Be_Opened_And_Contains_Valid_Schema()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(localAppData, "WeighBridge Modern", "Data", "weighbridge.db");

        output.WriteLine($"Database Path: {dbPath}");
        if (!File.Exists(dbPath))
        {
            output.WriteLine("Runtime database does not exist yet at this location.");
            return;
        }

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var tables = new[] { "__EFMigrationsHistory", "Weighments", "TicketReservations", "Vehicles", "Parties", "Materials", "VehicleTypes" };

        foreach (var t in tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {t};";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            output.WriteLine($"Table {t}: {count} row(s)");
        }

        // Check weighments columns
        using var colsCmd = conn.CreateCommand();
        colsCmd.CommandText = "PRAGMA table_info(Weighments);";
        using var colsReader = colsCmd.ExecuteReader();
        output.WriteLine("\nColumns of Weighments table:");
        while (colsReader.Read())
        {
            output.WriteLine($"  {colsReader["name"]} ({colsReader["type"]})");
        }

        // Check weighments rows
        using var wCmd = conn.CreateCommand();
        wCmd.CommandText = "SELECT Id, SlipNumber, VehicleNumber, Status, ReservationId FROM Weighments ORDER BY Id DESC;";
        using var wReader = wCmd.ExecuteReader();
        output.WriteLine("\nExisting Weighments in Database:");
        while (wReader.Read())
        {
            output.WriteLine($"  Id={wReader["Id"]}, Slip={wReader["SlipNumber"]}, Vehicle={wReader["VehicleNumber"]}, Status={wReader["Status"]}, ReservationId={wReader["ReservationId"]}");
        }

        // Check recent reservations
        using var rCmd = conn.CreateCommand();
        rCmd.CommandText = "SELECT Id, SlipNumber, TentativeVehicleNumber, Status, ConsumedByWeighmentId FROM TicketReservations ORDER BY Id DESC LIMIT 5;";
        using var rReader = rCmd.ExecuteReader();
        output.WriteLine("\nRecent Ticket Reservations in Database:");
        while (rReader.Read())
        {
            output.WriteLine($"  Id={rReader["Id"]}, Slip={rReader["SlipNumber"]}, Tentative={rReader["TentativeVehicleNumber"]}, Status={rReader["Status"]}, ConsumedBy={rReader["ConsumedByWeighmentId"]}");
        }
    }
}
