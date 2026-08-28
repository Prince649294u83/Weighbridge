using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

public sealed class MaterialService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IEventPublisher events,
    ILogger<MaterialService> logger) : IMaterialService
{
    private const string ModuleName = "Masters";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IEventPublisher _events = events
        ?? throw new ArgumentNullException(nameof(events));
    private readonly ILogger<MaterialService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Material> CreateAsync(CreateMaterialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedName = request.Name.Trim();

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Material>();

        var existing = await repository.FindAsync(
            m => m.Name == trimmedName && m.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"An active material named '{trimmedName}' already exists.");
        }

        var material = Material.Create(
            request.Name,
            request.Code,
            request.Description);

        material.CreatedBy = _permissions.CurrentOperator.UserName;

        await repository.AddAsync(material, cancellationToken).ConfigureAwait(false);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Material {Name} (ID {Id}) created by {Operator}",
            material.Name, material.Id, material.CreatedBy);

        _events.Publish(new MaterialCreatedEvent(material.Id, material.Name, ModuleName));

        return material;
    }

    public async Task<Material> UpdateAsync(UpdateMaterialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedName = request.Name.Trim();

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Material>();

        var material = await repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Material ID {request.Id} was not found.");

        var existing = await repository.FindAsync(
            m => m.Id != request.Id && m.Name == trimmedName && m.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active material named '{trimmedName}' already exists.");
        }

        material.Update(
            request.Name,
            request.Code,
            request.Description);

        material.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(material);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Material {Name} (ID {Id}) updated by {Operator}",
            material.Name, material.Id, material.ModifiedBy);

        _events.Publish(new MaterialUpdatedEvent(material.Id, material.Name, ModuleName));

        return material;
    }

    public async Task<Material> DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Material>();

        var material = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Material ID {id} was not found.");

        material.Deactivate();
        material.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(material);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Material {Name} (ID {Id}) deactivated by {Operator}",
            material.Name, material.Id, material.ModifiedBy);

        _events.Publish(new MaterialDeactivatedEvent(material.Id, material.Name, ModuleName));

        return material;
    }

    public async Task<Material> ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Material>();

        var material = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Material ID {id} was not found.");

        var existing = await repository.FindAsync(
            m => m.Id != id && m.Name == material.Name && m.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active material named '{material.Name}' already exists.");
        }

        material.Reactivate();
        material.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(material);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Material {Name} (ID {Id}) reactivated by {Operator}",
            material.Name, material.Id, material.ModifiedBy);

        _events.Publish(new MaterialReactivatedEvent(material.Id, material.Name, ModuleName));

        return material;
    }

    public async Task<Material?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<Material>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Material?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        await using var unitOfWork = _unitOfWork();
        var matches = await unitOfWork.Repository<Material>().FindAsync(
            m => m.Name == trimmed && m.IsActive,
            cancellationToken).ConfigureAwait(false);

        return matches.Count > 0 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Material>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return includeInactive
            ? await unitOfWork.Repository<Material>().GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await unitOfWork.Repository<Material>().FindAsync(m => m.IsActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Material>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        }

        var trimmed = query.Trim().ToUpperInvariant();
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Material>();

        return includeInactive
            ? await repository.FindAsync(m => m.Name.ToUpper().Contains(trimmed) || (m.Code != null && m.Code.ToUpper().Contains(trimmed)), cancellationToken).ConfigureAwait(false)
            : await repository.FindAsync(m => m.IsActive && (m.Name.ToUpper().Contains(trimmed) || (m.Code != null && m.Code.ToUpper().Contains(trimmed))), cancellationToken).ConfigureAwait(false);
    }
}
