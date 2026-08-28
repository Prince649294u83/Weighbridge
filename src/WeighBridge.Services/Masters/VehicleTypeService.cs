using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

public sealed class VehicleTypeService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IEventPublisher events,
    ILogger<VehicleTypeService> logger) : IVehicleTypeService
{
    private const string ModuleName = "Masters";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IEventPublisher _events = events
        ?? throw new ArgumentNullException(nameof(events));
    private readonly ILogger<VehicleTypeService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<VehicleType> CreateAsync(CreateVehicleTypeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalisedName = VehicleType.NormaliseTypeName(request.TypeName);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<VehicleType>();

        var existing = await repository.FindAsync(
            vt => vt.TypeName == normalisedName && vt.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"A vehicle type named '{normalisedName}' already exists.");
        }

        var vehicleType = VehicleType.Create(request.TypeName, request.Description);
        vehicleType.CreatedBy = _permissions.CurrentOperator.UserName;

        await repository.AddAsync(vehicleType, cancellationToken).ConfigureAwait(false);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle type {TypeName} (ID {Id}) created by {Operator}",
            vehicleType.TypeName, vehicleType.Id, vehicleType.CreatedBy);

        _events.Publish(new VehicleTypeCreatedEvent(vehicleType.Id, vehicleType.TypeName, ModuleName));

        return vehicleType;
    }

    public async Task<VehicleType> UpdateAsync(UpdateVehicleTypeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalisedName = VehicleType.NormaliseTypeName(request.TypeName);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<VehicleType>();

        var vehicleType = await repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle type ID {request.Id} was not found.");

        var existing = await repository.FindAsync(
            vt => vt.Id != request.Id && vt.TypeName == normalisedName && vt.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active vehicle type named '{normalisedName}' already exists.");
        }

        vehicleType.Update(request.TypeName, request.Description);
        vehicleType.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicleType);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle type {TypeName} (ID {Id}) updated by {Operator}",
            vehicleType.TypeName, vehicleType.Id, vehicleType.ModifiedBy);

        _events.Publish(new VehicleTypeUpdatedEvent(vehicleType.Id, vehicleType.TypeName, ModuleName));

        return vehicleType;
    }

    public async Task<VehicleType> DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<VehicleType>();

        var vehicleType = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle type ID {id} was not found.");

        vehicleType.Deactivate();
        vehicleType.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicleType);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle type {TypeName} (ID {Id}) deactivated by {Operator}",
            vehicleType.TypeName, vehicleType.Id, vehicleType.ModifiedBy);

        _events.Publish(new VehicleTypeDeactivatedEvent(vehicleType.Id, vehicleType.TypeName, ModuleName));

        return vehicleType;
    }

    public async Task<VehicleType> ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<VehicleType>();

        var vehicleType = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vehicle type ID {id} was not found.");

        var existing = await repository.FindAsync(
            vt => vt.Id != id && vt.TypeName == vehicleType.TypeName && vt.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active vehicle type named '{vehicleType.TypeName}' already exists.");
        }

        vehicleType.Reactivate();
        vehicleType.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(vehicleType);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Vehicle type {TypeName} (ID {Id}) reactivated by {Operator}",
            vehicleType.TypeName, vehicleType.Id, vehicleType.ModifiedBy);

        _events.Publish(new VehicleTypeReactivatedEvent(vehicleType.Id, vehicleType.TypeName, ModuleName));

        return vehicleType;
    }

    public async Task<VehicleType?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<VehicleType>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VehicleType>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return includeInactive
            ? await unitOfWork.Repository<VehicleType>().GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await unitOfWork.Repository<VehicleType>().FindAsync(vt => vt.IsActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VehicleType>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        }

        var trimmed = query.Trim().ToUpperInvariant();
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<VehicleType>();

        return includeInactive
            ? await repository.FindAsync(vt => vt.TypeName.ToUpper().Contains(trimmed), cancellationToken).ConfigureAwait(false)
            : await repository.FindAsync(vt => vt.IsActive && vt.TypeName.ToUpper().Contains(trimmed), cancellationToken).ConfigureAwait(false);
    }
}
