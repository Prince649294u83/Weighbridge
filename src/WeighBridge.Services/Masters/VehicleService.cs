using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

public sealed class VehicleService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IEventPublisher events,
    ILogger<VehicleService> logger) : IVehicleService
{
    private const string ModuleName = "Masters";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IEventPublisher _events = events
        ?? throw new ArgumentNullException(nameof(events));
    private readonly ILogger<VehicleService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Vehicle> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalisedNumber = Vehicle.NormaliseVehicleNumber(request.VehicleNumber);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Vehicle>();

        var existing = await repository.FindAsync(
            v => v.VehicleNumber == normalisedNumber && v.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"An active vehicle with registration '{normalisedNumber}' already exists.");
        }

        if (request.VehicleTypeId.HasValue)
        {
            var typeRepo = unitOfWork.Repository<VehicleType>();
            var typeExists = await typeRepo.GetByIdAsync(request.VehicleTypeId.Value, cancellationToken).ConfigureAwait(false);
            if (typeExists is null)
            {
                throw new InvalidOperationException($"Vehicle type ID {request.VehicleTypeId.Value} does not exist.");
            }
        }

        var vehicle = Vehicle.Create(
            request.VehicleNumber,
            request.VehicleTypeId,
            request.TareWeightKg,
            request.Remarks);

        vehicle.CreatedBy = _permissions.CurrentOperator.UserName;

        await repository.AddAsync(vehicle, cancellationToken).ConfigureAwait(false);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle {VehicleNumber} (ID {Id}) created by {Operator}",
            vehicle.VehicleNumber, vehicle.Id, vehicle.CreatedBy);

        _events.Publish(new VehicleCreatedEvent(vehicle.Id, vehicle.VehicleNumber, ModuleName));

        return vehicle;
    }

    public async Task<Vehicle> UpdateAsync(UpdateVehicleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalisedNumber = Vehicle.NormaliseVehicleNumber(request.VehicleNumber);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Vehicle>();

        var vehicle = await repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle ID {request.Id} was not found.");

        var existing = await repository.FindAsync(
            v => v.Id != request.Id && v.VehicleNumber == normalisedNumber && v.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active vehicle with registration '{normalisedNumber}' already exists.");
        }

        if (request.VehicleTypeId.HasValue)
        {
            var typeRepo = unitOfWork.Repository<VehicleType>();
            var typeExists = await typeRepo.GetByIdAsync(request.VehicleTypeId.Value, cancellationToken).ConfigureAwait(false);
            if (typeExists is null)
            {
                throw new InvalidOperationException($"Vehicle type ID {request.VehicleTypeId.Value} does not exist.");
            }
        }

        vehicle.Update(
            request.VehicleNumber,
            request.VehicleTypeId,
            request.TareWeightKg,
            request.Remarks);

        vehicle.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicle);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle {VehicleNumber} (ID {Id}) updated by {Operator}",
            vehicle.VehicleNumber, vehicle.Id, vehicle.ModifiedBy);

        _events.Publish(new VehicleUpdatedEvent(vehicle.Id, vehicle.VehicleNumber, ModuleName));

        return vehicle;
    }

    public async Task<Vehicle> DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Vehicle>();

        var vehicle = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle ID {id} was not found.");

        vehicle.Deactivate();
        vehicle.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicle);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle {VehicleNumber} (ID {Id}) deactivated by {Operator}",
            vehicle.VehicleNumber, vehicle.Id, vehicle.ModifiedBy);

        _events.Publish(new VehicleDeactivatedEvent(vehicle.Id, vehicle.VehicleNumber, ModuleName));

        return vehicle;
    }

    public async Task<Vehicle> ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Vehicle>();

        var vehicle = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle ID {id} was not found.");

        var existing = await repository.FindAsync(
            v => v.Id != id && v.VehicleNumber == vehicle.VehicleNumber && v.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active vehicle with registration '{vehicle.VehicleNumber}' already exists.");
        }

        vehicle.Reactivate();
        vehicle.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicle);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle {VehicleNumber} (ID {Id}) reactivated by {Operator}",
            vehicle.VehicleNumber, vehicle.Id, vehicle.ModifiedBy);

        _events.Publish(new VehicleReactivatedEvent(vehicle.Id, vehicle.VehicleNumber, ModuleName));

        return vehicle;
    }

    public async Task<Vehicle?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<Vehicle>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Vehicle?> GetByVehicleNumberAsync(string vehicleNumber, CancellationToken cancellationToken = default)
    {
        var normalised = Vehicle.NormaliseVehicleNumber(vehicleNumber);
        await using var unitOfWork = _unitOfWork();
        var matches = await unitOfWork.Repository<Vehicle>().FindAsync(
            v => v.VehicleNumber == normalised && v.IsActive,
            cancellationToken).ConfigureAwait(false);

        return matches.Count > 0 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Vehicle>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return includeInactive
            ? await unitOfWork.Repository<Vehicle>().GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await unitOfWork.Repository<Vehicle>().FindAsync(v => v.IsActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Vehicle>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        }

        var trimmed = query.Trim().ToUpperInvariant();
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Vehicle>();

        return includeInactive
            ? await repository.FindAsync(v => v.VehicleNumber.Contains(trimmed), cancellationToken).ConfigureAwait(false)
            : await repository.FindAsync(v => v.IsActive && v.VehicleNumber.Contains(trimmed), cancellationToken).ConfigureAwait(false);
    }
}
