using WeighBridge.Domain.Masters;

namespace WeighBridge.Core.Abstractions;

public sealed record CreateVehicleRequest(
    string VehicleNumber,
    long? VehicleTypeId = null,
    decimal? TareWeightKg = null,
    string? Remarks = null);

public sealed record UpdateVehicleRequest(
    long Id,
    string VehicleNumber,
    long? VehicleTypeId = null,
    decimal? TareWeightKg = null,
    string? Remarks = null);

public interface IVehicleService
{
    Task<Vehicle> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default);

    Task<Vehicle> UpdateAsync(UpdateVehicleRequest request, CancellationToken cancellationToken = default);

    Task<Vehicle> DeactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Vehicle> ReactivateAsync(long id, CancellationToken cancellationToken = default);

    Task<Vehicle?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Vehicle?> GetByVehicleNumberAsync(string vehicleNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> SearchAsync(string? query, bool includeInactive = false, CancellationToken cancellationToken = default);
}
