using WeighBridge.Domain.Masters;

namespace WeighBridge.Core.Abstractions;

public sealed record CreatePartyRequest(
    string Name,
    string? Code = null,
    string? Address = null,
    string? ContactNumber = null,
    string? Email = null,
    string? Remarks = null);

public sealed record UpdatePartyRequest(
    long Id,
    string Name,
    string? Code = null,
    string? Address = null,
    string? ContactNumber = null,
    string? Email = null,
    string? Remarks = null);

public interface IPartyService
{
    Task<Party> CreateAsync(CreatePartyRequest request, CancellationToken cancellationToken = default);

    Task<Party> UpdateAsync(UpdatePartyRequest request, CancellationToken cancellationToken = default);

    Task<Party> DeactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Party> ReactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Party?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Party?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Party>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Party>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default);
}
