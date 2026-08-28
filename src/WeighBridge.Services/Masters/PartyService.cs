using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

public sealed class PartyService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IEventPublisher events,
    ILogger<PartyService> logger) : IPartyService
{
    private const string ModuleName = "Masters";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions
        ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IEventPublisher _events = events
        ?? throw new ArgumentNullException(nameof(events));
    private readonly ILogger<PartyService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Party> CreateAsync(CreatePartyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedName = request.Name.Trim();

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Party>();

        var existing = await repository.FindAsync(
            p => p.Name == trimmedName && p.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"An active party named '{trimmedName}' already exists.");
        }

        var party = Party.Create(
            request.Name,
            request.Code,
            request.Address,
            request.ContactNumber,
            request.Email,
            request.Remarks);

        party.CreatedBy = _permissions.CurrentOperator.UserName;

        await repository.AddAsync(party, cancellationToken).ConfigureAwait(false);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Party {Name} (ID {Id}) created by {Operator}",
            party.Name, party.Id, party.CreatedBy);

        _events.Publish(new PartyCreatedEvent(party.Id, party.Name, ModuleName));

        return party;
    }

    public async Task<Party> UpdateAsync(UpdatePartyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedName = request.Name.Trim();

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Party>();

        var party = await repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Party ID {request.Id} was not found.");

        var existing = await repository.FindAsync(
            p => p.Id != request.Id && p.Name == trimmedName && p.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active party named '{trimmedName}' already exists.");
        }

        party.Update(
            request.Name,
            request.Code,
            request.Address,
            request.ContactNumber,
            request.Email,
            request.Remarks);

        party.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(party);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Party {Name} (ID {Id}) updated by {Operator}",
            party.Name, party.Id, party.ModifiedBy);

        _events.Publish(new PartyUpdatedEvent(party.Id, party.Name, ModuleName));

        return party;
    }

    public async Task<Party> DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Party>();

        var party = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Party ID {id} was not found.");

        party.Deactivate();
        party.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(party);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Party {Name} (ID {Id}) deactivated by {Operator}",
            party.Name, party.Id, party.ModifiedBy);

        _events.Publish(new PartyDeactivatedEvent(party.Id, party.Name, ModuleName));

        return party;
    }

    public async Task<Party> ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Party>();

        var party = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Party ID {id} was not found.");

        var existing = await repository.FindAsync(
            p => p.Id != id && p.Name == party.Name && p.IsActive,
            cancellationToken).ConfigureAwait(false);

        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"Another active party named '{party.Name}' already exists.");
        }

        party.Reactivate();
        party.ModifiedBy = _permissions.CurrentOperator.UserName;

        repository.Update(party);
        await UniquenessGuardedSave.SaveChangesAsync(
            unitOfWork,
            "An active record with the same details already exists (it may have been created on another screen just now).",
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Party {Name} (ID {Id}) reactivated by {Operator}",
            party.Name, party.Id, party.ModifiedBy);

        _events.Publish(new PartyReactivatedEvent(party.Id, party.Name, ModuleName));

        return party;
    }

    public async Task<Party?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<Party>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Party?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        await using var unitOfWork = _unitOfWork();
        var matches = await unitOfWork.Repository<Party>().FindAsync(
            p => p.Name == trimmed && p.IsActive,
            cancellationToken).ConfigureAwait(false);

        return matches.Count > 0 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Party>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return includeInactive
            ? await unitOfWork.Repository<Party>().GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await unitOfWork.Repository<Party>().FindAsync(p => p.IsActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Party>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        }

        var trimmed = query.Trim().ToUpperInvariant();
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Party>();

        return includeInactive
            ? await repository.FindAsync(p => p.Name.ToUpper().Contains(trimmed) || (p.Code != null && p.Code.ToUpper().Contains(trimmed)), cancellationToken).ConfigureAwait(false)
            : await repository.FindAsync(p => p.IsActive && (p.Name.ToUpper().Contains(trimmed) || (p.Code != null && p.Code.ToUpper().Contains(trimmed))), cancellationToken).ConfigureAwait(false);
    }
}
