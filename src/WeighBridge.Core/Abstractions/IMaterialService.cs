using WeighBridge.Domain.Masters;

namespace WeighBridge.Core.Abstractions;

public sealed record CreateMaterialRequest(
    string Name,
    string? Code = null,
    string? Description = null);

public sealed record UpdateMaterialRequest(
    long Id,
    string Name,
    string? Code = null,
    string? Description = null);

public interface IMaterialService
{
    Task<Material> CreateAsync(CreateMaterialRequest request, CancellationToken cancellationToken = default);

    Task<Material> UpdateAsync(UpdateMaterialRequest request, CancellationToken cancellationToken = default);

    Task<Material> DeactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Material> ReactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Material?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Material?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Material>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Material>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default);
}
