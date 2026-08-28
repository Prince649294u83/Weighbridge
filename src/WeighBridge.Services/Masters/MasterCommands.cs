using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Security;
using WeighBridge.Core.Validation;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

#region Vehicle Commands

public sealed class CreateVehicleCommand(IVehicleService service, CreateVehicleRequest request)
    : IApplicationCommand<Vehicle>, IValidatable, IRequiresPermission
{
    private static readonly CreateVehicleRequestValidator Rules = new();
    private readonly IVehicleService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly CreateVehicleRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Create vehicle";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Vehicle>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Creating vehicle record…");

        var vehicle = await _service.CreateAsync(_request, context.CancellationToken).ConfigureAwait(false);

        context.Audit("VehicleNumber", vehicle.VehicleNumber)
            .Audit("VehicleTypeId", vehicle.VehicleTypeId?.ToString() ?? "None")
            .Audit("TareWeightKg", vehicle.TareWeightKg?.ToString() ?? "None");

        return CommandResult<Vehicle>.Success(vehicle, $"Vehicle {vehicle.VehicleNumber} created.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class UpdateVehicleCommand(IVehicleService service, UpdateVehicleRequest request)
    : IApplicationCommand<Vehicle>, IValidatable, IRequiresPermission
{
    private static readonly UpdateVehicleRequestValidator Rules = new();
    private readonly IVehicleService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly UpdateVehicleRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Update vehicle";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Vehicle>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Updating vehicle record…");

        var vehicle = await _service.UpdateAsync(_request, context.CancellationToken).ConfigureAwait(false);

        context.Audit("VehicleNumber", vehicle.VehicleNumber)
            .Audit("VehicleTypeId", vehicle.VehicleTypeId?.ToString() ?? "None")
            .Audit("TareWeightKg", vehicle.TareWeightKg?.ToString() ?? "None");

        return CommandResult<Vehicle>.Success(vehicle, $"Vehicle {vehicle.VehicleNumber} updated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class DeactivateVehicleCommand(IVehicleService service, long vehicleId)
    : IApplicationCommand<Vehicle>, IRequiresPermission
{
    private readonly IVehicleService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Deactivate vehicle";
    public Permission? RequiredPermission => Permissions.MastersDelete;

    public async Task<CommandResult<Vehicle>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Deactivating vehicle…");

        var vehicle = await _service.DeactivateAsync(vehicleId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("VehicleNumber", vehicle.VehicleNumber).Audit("Action", "Deactivate");

        return CommandResult<Vehicle>.Success(vehicle, $"Vehicle {vehicle.VehicleNumber} deactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class ReactivateVehicleCommand(IVehicleService service, long vehicleId)
    : IApplicationCommand<Vehicle>, IRequiresPermission
{
    private readonly IVehicleService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Reactivate vehicle";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public async Task<CommandResult<Vehicle>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Reactivating vehicle…");

        var vehicle = await _service.ReactivateAsync(vehicleId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("VehicleNumber", vehicle.VehicleNumber).Audit("Action", "Reactivate");

        return CommandResult<Vehicle>.Success(vehicle, $"Vehicle {vehicle.VehicleNumber} reactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

#endregion

#region Party Commands

public sealed class CreatePartyCommand(IPartyService service, CreatePartyRequest request)
    : IApplicationCommand<Party>, IValidatable, IRequiresPermission
{
    private static readonly CreatePartyRequestValidator Rules = new();
    private readonly IPartyService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly CreatePartyRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Create party";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Party>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Creating party record…");

        var party = await _service.CreateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("PartyName", party.Name).Audit("PartyCode", party.Code ?? "None");

        return CommandResult<Party>.Success(party, $"Party '{party.Name}' created.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class UpdatePartyCommand(IPartyService service, UpdatePartyRequest request)
    : IApplicationCommand<Party>, IValidatable, IRequiresPermission
{
    private static readonly UpdatePartyRequestValidator Rules = new();
    private readonly IPartyService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly UpdatePartyRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Update party";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Party>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Updating party record…");

        var party = await _service.UpdateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("PartyName", party.Name).Audit("PartyCode", party.Code ?? "None");

        return CommandResult<Party>.Success(party, $"Party '{party.Name}' updated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class DeactivatePartyCommand(IPartyService service, long partyId)
    : IApplicationCommand<Party>, IRequiresPermission
{
    private readonly IPartyService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Deactivate party";
    public Permission? RequiredPermission => Permissions.MastersDelete;

    public async Task<CommandResult<Party>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Deactivating party…");

        var party = await _service.DeactivateAsync(partyId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("PartyName", party.Name).Audit("Action", "Deactivate");

        return CommandResult<Party>.Success(party, $"Party '{party.Name}' deactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class ReactivatePartyCommand(IPartyService service, long partyId)
    : IApplicationCommand<Party>, IRequiresPermission
{
    private readonly IPartyService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Reactivate party";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public async Task<CommandResult<Party>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Reactivating party…");

        var party = await _service.ReactivateAsync(partyId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("PartyName", party.Name).Audit("Action", "Reactivate");

        return CommandResult<Party>.Success(party, $"Party '{party.Name}' reactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

#endregion

#region Material Commands

public sealed class CreateMaterialCommand(IMaterialService service, CreateMaterialRequest request)
    : IApplicationCommand<Material>, IValidatable, IRequiresPermission
{
    private static readonly CreateMaterialRequestValidator Rules = new();
    private readonly IMaterialService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly CreateMaterialRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Create material";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Material>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Creating material record…");

        var material = await _service.CreateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("MaterialName", material.Name).Audit("MaterialCode", material.Code ?? "None");

        return CommandResult<Material>.Success(material, $"Material '{material.Name}' created.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class UpdateMaterialCommand(IMaterialService service, UpdateMaterialRequest request)
    : IApplicationCommand<Material>, IValidatable, IRequiresPermission
{
    private static readonly UpdateMaterialRequestValidator Rules = new();
    private readonly IMaterialService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly UpdateMaterialRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Update material";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<Material>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Updating material record…");

        var material = await _service.UpdateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("MaterialName", material.Name).Audit("MaterialCode", material.Code ?? "None");

        return CommandResult<Material>.Success(material, $"Material '{material.Name}' updated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class DeactivateMaterialCommand(IMaterialService service, long materialId)
    : IApplicationCommand<Material>, IRequiresPermission
{
    private readonly IMaterialService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Deactivate material";
    public Permission? RequiredPermission => Permissions.MastersDelete;

    public async Task<CommandResult<Material>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Deactivating material…");

        var material = await _service.DeactivateAsync(materialId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("MaterialName", material.Name).Audit("Action", "Deactivate");

        return CommandResult<Material>.Success(material, $"Material '{material.Name}' deactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class ReactivateMaterialCommand(IMaterialService service, long materialId)
    : IApplicationCommand<Material>, IRequiresPermission
{
    private readonly IMaterialService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Reactivate material";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public async Task<CommandResult<Material>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Reactivating material…");

        var material = await _service.ReactivateAsync(materialId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("MaterialName", material.Name).Audit("Action", "Reactivate");

        return CommandResult<Material>.Success(material, $"Material '{material.Name}' reactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

#endregion

#region VehicleType Commands

public sealed class CreateVehicleTypeCommand(IVehicleTypeService service, CreateVehicleTypeRequest request)
    : IApplicationCommand<VehicleType>, IValidatable, IRequiresPermission
{
    private static readonly CreateVehicleTypeRequestValidator Rules = new();
    private readonly IVehicleTypeService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly CreateVehicleTypeRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Create vehicle type";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<VehicleType>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Creating vehicle type…");

        var vehicleType = await _service.CreateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("TypeName", vehicleType.TypeName);

        return CommandResult<VehicleType>.Success(vehicleType, $"Vehicle type '{vehicleType.TypeName}' created.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class UpdateVehicleTypeCommand(IVehicleTypeService service, UpdateVehicleTypeRequest request)
    : IApplicationCommand<VehicleType>, IValidatable, IRequiresPermission
{
    private static readonly UpdateVehicleTypeRequestValidator Rules = new();
    private readonly IVehicleTypeService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly UpdateVehicleTypeRequest _request = request ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Update vehicle type";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    public async Task<CommandResult<VehicleType>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Updating vehicle type…");

        var vehicleType = await _service.UpdateAsync(_request, context.CancellationToken).ConfigureAwait(false);
        context.Audit("TypeName", vehicleType.TypeName);

        return CommandResult<VehicleType>.Success(vehicleType, $"Vehicle type '{vehicleType.TypeName}' updated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class DeactivateVehicleTypeCommand(IVehicleTypeService service, long vehicleTypeId)
    : IApplicationCommand<VehicleType>, IRequiresPermission
{
    private readonly IVehicleTypeService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Deactivate vehicle type";
    public Permission? RequiredPermission => Permissions.MastersDelete;

    public async Task<CommandResult<VehicleType>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Deactivating vehicle type…");

        var vehicleType = await _service.DeactivateAsync(vehicleTypeId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("TypeName", vehicleType.TypeName).Audit("Action", "Deactivate");

        return CommandResult<VehicleType>.Success(vehicleType, $"Vehicle type '{vehicleType.TypeName}' deactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

public sealed class ReactivateVehicleTypeCommand(IVehicleTypeService service, long vehicleTypeId)
    : IApplicationCommand<VehicleType>, IRequiresPermission
{
    private readonly IVehicleTypeService _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "Reactivate vehicle type";
    public Permission? RequiredPermission => Permissions.MastersEdit;

    public async Task<CommandResult<VehicleType>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ReportStatus("Reactivating vehicle type…");

        var vehicleType = await _service.ReactivateAsync(vehicleTypeId, context.CancellationToken).ConfigureAwait(false);
        context.Audit("TypeName", vehicleType.TypeName).Audit("Action", "Reactivate");

        return CommandResult<VehicleType>.Success(vehicleType, $"Vehicle type '{vehicleType.TypeName}' reactivated.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

#endregion
