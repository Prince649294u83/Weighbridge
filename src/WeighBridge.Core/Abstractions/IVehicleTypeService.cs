using WeighBridge.Domain.Masters;

namespace WeighBridge.Core.Abstractions;

public sealed record CreateVehicleTypeRequest(string TypeName, string? Description = null);

public sealed record UpdateVehicleTypeRequest(long Id, string TypeName, string? Description = null);

public interface IVehicleTypeService
{
    Task<VehicleType> CreateAsync(CreateVehicleTypeRequest request, CancellationToken cancellationToken = default);

    Task<VehicleType> UpdateAsync(UpdateVehicleTypeRequest request, CancellationToken cancellationToken = default);

    Task<VehicleType> DeactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<VehicleType> ReactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<VehicleType?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleType>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleType>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default);
}
