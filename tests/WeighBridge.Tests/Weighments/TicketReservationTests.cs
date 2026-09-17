using Microsoft.EntityFrameworkCore;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using Xunit;

namespace WeighBridge.Tests.Weighments;

public sealed class TicketReservationTests
{
    [Fact]
    public void Create_InitializesInReservedStatus_WithUniqueVersion()
    {
        var reservation = TicketReservation.Create(
            tentativeVehicleNumber: "ka-01-ab-1234",
            terminalId: "TERM-01",
            operatorName: "operator1");

        Assert.Equal(TicketReservationStatus.Reserved, reservation.Status);
        Assert.Equal("KA01AB1234", reservation.TentativeVehicleNumber);
        Assert.Equal("TERM-01", reservation.TerminalId);
        Assert.Equal("operator1", reservation.CreatedBy);
        Assert.NotEqual(Guid.Empty, reservation.Version);
        Assert.Empty(reservation.SlipNumber);
        Assert.Null(reservation.ConsumedByWeighmentId);
        Assert.Null(reservation.ConsumedAtUtc);
        Assert.Null(reservation.CancelledAtUtc);
    }

    [Fact]
    public void AssignSlipNumber_ThrowsIfTransient()
    {
        var reservation = TicketReservation.Create();
        Assert.Throws<InvalidOperationException>(() => reservation.AssignSlipNumber());
    }

    [Fact]
    public void AssignSlipNumber_DerivesCanonicalSlipNumber_FromId()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 42;

        reservation.AssignSlipNumber();

        Assert.Equal("WB-000042", reservation.SlipNumber);
    }

    [Fact]
    public void AssignSlipNumber_ThrowsIfAlreadyAssigned()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 42;
        reservation.AssignSlipNumber();

        Assert.Throws<InvalidOperationException>(() => reservation.AssignSlipNumber());
    }

    [Fact]
    public void Consume_TransitionsToConsumed_AndRecordsAudit()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 10;
        reservation.AssignSlipNumber();

        reservation.Consume(weighmentId: 100, consumedBy: "operator1");

        Assert.Equal(TicketReservationStatus.Consumed, reservation.Status);
        Assert.Equal(100, reservation.ConsumedByWeighmentId);
        Assert.Equal("operator1", reservation.ConsumedBy);
        Assert.NotNull(reservation.ConsumedAtUtc);
    }

    [Fact]
    public void Consume_ThrowsIfAlreadyConsumed()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 10;
        reservation.AssignSlipNumber();
        reservation.Consume(weighmentId: 100);

        Assert.Throws<InvalidOperationException>(() => reservation.Consume(weighmentId: 101));
    }

    [Fact]
    public void Cancel_TransitionsToCancelled_AndRecordsReason()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 10;
        reservation.AssignSlipNumber();

        reservation.Cancel("Operator cleared form", "operator1");

        Assert.Equal(TicketReservationStatus.Cancelled, reservation.Status);
        Assert.Equal("Operator cleared form", reservation.CancellationReason);
        Assert.Equal("operator1", reservation.ModifiedBy);
        Assert.NotNull(reservation.CancelledAtUtc);
    }

    [Fact]
    public void Cancel_ThrowsIfAlreadyConsumed()
    {
        var reservation = TicketReservation.Create();
        reservation.Id = 10;
        reservation.AssignSlipNumber();
        reservation.Consume(100);

        Assert.Throws<InvalidOperationException>(() => reservation.Cancel("reason"));
    }

    [Fact]
    public void OpenWithReservedSlip_EnforcesValidation_AndSetsReservationProperties()
    {
        var weighment = Weighment.OpenWithReservedSlip(
            slipNumber: "wb-000123",
            reservationId: 123,
            vehicleNumber: "ka-01-ab-1234",
            mode: WeighmentMode.GrossFirst,
            partyName: " Acme Corp ",
            charges: 50m);

        Assert.Equal("WB-000123", weighment.SlipNumber);
        Assert.Equal(123, weighment.ReservationId);
        Assert.Equal("KA01AB1234", weighment.VehicleNumber);
        Assert.Equal("Acme Corp", weighment.PartyName);
        Assert.Equal(50m, weighment.Charges);
        Assert.Equal(WeighmentStatus.Created, weighment.Status);
    }

    [Fact]
    public void OpenWithReservedSlip_ThrowsOnInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Weighment.OpenWithReservedSlip("WB-000001", 0, "KA01AB1234", WeighmentMode.GrossFirst));

        Assert.Throws<ArgumentException>(() =>
            Weighment.OpenWithReservedSlip("INVALID-SLIP", 1, "KA01AB1234", WeighmentMode.GrossFirst));

        Assert.Throws<ArgumentException>(() =>
            Weighment.OpenWithReservedSlip("WB-000001", 1, "", WeighmentMode.GrossFirst));
    }

    [Fact]
    public async Task RealSqlite_ReservationAndWeighmentLifecycle_PersistsCorrectly()
    {
        using var harness = new WeighmentHarness();

        // 1. Create and persist TicketReservation in SQLite
        long reservationId;
        string reservedSlipNumber;

        await using (var uow = new UnitOfWork(harness.CreateContext(), harness.Operator))
        {
            var repo = uow.Repository<TicketReservation>();
            var reservation = TicketReservation.Create("MH12AB1234", "TERM-01", "TestOperator");
            await repo.AddAsync(reservation);
            await uow.SaveChangesAsync();

            reservation.AssignSlipNumber();
            await uow.SaveChangesAsync();

            reservationId = reservation.Id;
            reservedSlipNumber = reservation.SlipNumber;
            Assert.True(reservationId > 0);
            Assert.Equal("WB-000001", reservedSlipNumber);
        }

        // 2. Consume reservation and create Weighment atomically in single transaction
        long weighmentId;
        await using (var uow = new UnitOfWork(harness.CreateContext(), harness.Operator))
        {
            await uow.BeginTransactionAsync();

            var resRepo = uow.Repository<TicketReservation>();
            var weighmentRepo = uow.Repository<Weighment>();

            var reservation = await resRepo.GetByIdAsync(reservationId);
            Assert.NotNull(reservation);
            Assert.Equal(TicketReservationStatus.Reserved, reservation.Status);

            var weighment = Weighment.OpenWithReservedSlip(
                reservedSlipNumber,
                reservationId,
                "MH12AB1234",
                WeighmentMode.GrossFirst,
                partyName: "Test Party",
                materialName: "Coal",
                charges: 100m);

            weighment.RecordFirstWeight(new WeightCapture(25000m, DateTime.UtcNow, WeightSource.Indicator));
            await weighmentRepo.AddAsync(weighment);
            await uow.SaveChangesAsync();

            weighmentId = weighment.Id;
            reservation.Consume(weighmentId, "TestOperator");
            await uow.SaveChangesAsync();

            await uow.CommitTransactionAsync();
        }

        // 3. Reload from fresh DbContext and verify relationships & persistence
        await using (var context = harness.CreateContext())
        {
            var savedReservation = await context.Set<TicketReservation>().SingleAsync(r => r.Id == reservationId);
            Assert.Equal(TicketReservationStatus.Consumed, savedReservation.Status);
            Assert.Equal(weighmentId, savedReservation.ConsumedByWeighmentId);
            Assert.Equal("WB-000001", savedReservation.SlipNumber);

            var savedWeighment = await context.Set<Weighment>().SingleAsync(w => w.Id == weighmentId);
            Assert.Equal("WB-000001", savedWeighment.SlipNumber);
            Assert.Equal(reservationId, savedWeighment.ReservationId);
            Assert.Equal(WeighmentStatus.AwaitingSecondWeight, savedWeighment.Status);
            Assert.Equal(25000m, savedWeighment.FirstWeight?.Kilograms);
            Assert.Equal("Test Party", savedWeighment.PartyName);
            Assert.Equal("Coal", savedWeighment.MaterialName);
        }
    }
}
