using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Validation;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Services.Masters;

#region Vehicle Validators

public sealed class CreateVehicleRequestValidator : Validator<CreateVehicleRequest>
{
    public CreateVehicleRequestValidator()
    {
        AddRule(
            nameof(CreateVehicleRequest.VehicleNumber),
            r => !string.IsNullOrWhiteSpace(r.VehicleNumber),
            "Vehicle registration number is required.");

        AddRule(
            nameof(CreateVehicleRequest.VehicleNumber),
            r => string.IsNullOrWhiteSpace(r.VehicleNumber)
                || Vehicle.NormaliseVehicleNumber(r.VehicleNumber).Length <= Vehicle.VehicleNumberMaxLength,
            $"Vehicle number cannot be longer than {Vehicle.VehicleNumberMaxLength} characters.");

        AddRule(
            nameof(CreateVehicleRequest.TareWeightKg),
            r => !r.TareWeightKg.HasValue || (r.TareWeightKg.Value >= 0 && r.TareWeightKg.Value <= Vehicle.MaximumTareKg),
            $"Tare weight must be between 0 and {Vehicle.MaximumTareKg:0} kg.");

        AddRule(
            nameof(CreateVehicleRequest.Remarks),
            r => string.IsNullOrWhiteSpace(r.Remarks) || r.Remarks.Trim().Length <= Vehicle.RemarksMaxLength,
            $"Remarks must be {Vehicle.RemarksMaxLength} characters or fewer.");
    }
}

public sealed class UpdateVehicleRequestValidator : Validator<UpdateVehicleRequest>
{
    public UpdateVehicleRequestValidator()
    {
        AddRule(
            nameof(UpdateVehicleRequest.Id),
            r => r.Id > 0,
            "A valid vehicle ID is required.");

        AddRule(
            nameof(UpdateVehicleRequest.VehicleNumber),
            r => !string.IsNullOrWhiteSpace(r.VehicleNumber),
            "Vehicle registration number is required.");

        AddRule(
            nameof(UpdateVehicleRequest.VehicleNumber),
            r => string.IsNullOrWhiteSpace(r.VehicleNumber)
                || Vehicle.NormaliseVehicleNumber(r.VehicleNumber).Length <= Vehicle.VehicleNumberMaxLength,
            $"Vehicle number cannot be longer than {Vehicle.VehicleNumberMaxLength} characters.");

        AddRule(
            nameof(UpdateVehicleRequest.TareWeightKg),
            r => !r.TareWeightKg.HasValue || (r.TareWeightKg.Value >= 0 && r.TareWeightKg.Value <= Vehicle.MaximumTareKg),
            $"Tare weight must be between 0 and {Vehicle.MaximumTareKg:0} kg.");

        AddRule(
            nameof(UpdateVehicleRequest.Remarks),
            r => string.IsNullOrWhiteSpace(r.Remarks) || r.Remarks.Trim().Length <= Vehicle.RemarksMaxLength,
            $"Remarks must be {Vehicle.RemarksMaxLength} characters or fewer.");
    }
}

#endregion

#region Party Validators

public sealed class CreatePartyRequestValidator : Validator<CreatePartyRequest>
{
    public CreatePartyRequestValidator()
    {
        AddRule(
            nameof(CreatePartyRequest.Name),
            r => !string.IsNullOrWhiteSpace(r.Name),
            "Party name is required.");

        AddRule(
            nameof(CreatePartyRequest.Name),
            r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length <= Party.NameMaxLength,
            $"Party name must be {Party.NameMaxLength} characters or fewer.");

        AddRule(
            nameof(CreatePartyRequest.Code),
            r => string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length <= Party.CodeMaxLength,
            $"Code must be {Party.CodeMaxLength} characters or fewer.");

        AddRule(
            nameof(CreatePartyRequest.Address),
            r => string.IsNullOrWhiteSpace(r.Address) || r.Address.Trim().Length <= Party.AddressMaxLength,
            $"Address must be {Party.AddressMaxLength} characters or fewer.");

        AddRule(
            nameof(CreatePartyRequest.ContactNumber),
            r => string.IsNullOrWhiteSpace(r.ContactNumber) || r.ContactNumber.Trim().Length <= Party.ContactNumberMaxLength,
            $"Contact number must be {Party.ContactNumberMaxLength} characters or fewer.");

        AddRule(
            nameof(CreatePartyRequest.Email),
            r => string.IsNullOrWhiteSpace(r.Email) || r.Email.Trim().Length <= Party.EmailMaxLength,
            $"Email must be {Party.EmailMaxLength} characters or fewer.");

        AddRule(
            nameof(CreatePartyRequest.Remarks),
            r => string.IsNullOrWhiteSpace(r.Remarks) || r.Remarks.Trim().Length <= Party.RemarksMaxLength,
            $"Remarks must be {Party.RemarksMaxLength} characters or fewer.");
    }
}

public sealed class UpdatePartyRequestValidator : Validator<UpdatePartyRequest>
{
    public UpdatePartyRequestValidator()
    {
        AddRule(
            nameof(UpdatePartyRequest.Id),
            r => r.Id > 0,
            "A valid party ID is required.");

        AddRule(
            nameof(UpdatePartyRequest.Name),
            r => !string.IsNullOrWhiteSpace(r.Name),
            "Party name is required.");

        AddRule(
            nameof(UpdatePartyRequest.Name),
            r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length <= Party.NameMaxLength,
            $"Party name must be {Party.NameMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdatePartyRequest.Code),
            r => string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length <= Party.CodeMaxLength,
            $"Code must be {Party.CodeMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdatePartyRequest.Address),
            r => string.IsNullOrWhiteSpace(r.Address) || r.Address.Trim().Length <= Party.AddressMaxLength,
            $"Address must be {Party.AddressMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdatePartyRequest.ContactNumber),
            r => string.IsNullOrWhiteSpace(r.ContactNumber) || r.ContactNumber.Trim().Length <= Party.ContactNumberMaxLength,
            $"Contact number must be {Party.ContactNumberMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdatePartyRequest.Email),
            r => string.IsNullOrWhiteSpace(r.Email) || r.Email.Trim().Length <= Party.EmailMaxLength,
            $"Email must be {Party.EmailMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdatePartyRequest.Remarks),
            r => string.IsNullOrWhiteSpace(r.Remarks) || r.Remarks.Trim().Length <= Party.RemarksMaxLength,
            $"Remarks must be {Party.RemarksMaxLength} characters or fewer.");
    }
}

#endregion

#region Material Validators

public sealed class CreateMaterialRequestValidator : Validator<CreateMaterialRequest>
{
    public CreateMaterialRequestValidator()
    {
        AddRule(
            nameof(CreateMaterialRequest.Name),
            r => !string.IsNullOrWhiteSpace(r.Name),
            "Material name is required.");

        AddRule(
            nameof(CreateMaterialRequest.Name),
            r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length <= Material.NameMaxLength,
            $"Material name must be {Material.NameMaxLength} characters or fewer.");

        AddRule(
            nameof(CreateMaterialRequest.Code),
            r => string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length <= Material.CodeMaxLength,
            $"Code must be {Material.CodeMaxLength} characters or fewer.");

        AddRule(
            nameof(CreateMaterialRequest.Description),
            r => string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length <= Material.DescriptionMaxLength,
            $"Description must be {Material.DescriptionMaxLength} characters or fewer.");
    }
}

public sealed class UpdateMaterialRequestValidator : Validator<UpdateMaterialRequest>
{
    public UpdateMaterialRequestValidator()
    {
        AddRule(
            nameof(UpdateMaterialRequest.Id),
            r => r.Id > 0,
            "A valid material ID is required.");

        AddRule(
            nameof(UpdateMaterialRequest.Name),
            r => !string.IsNullOrWhiteSpace(r.Name),
            "Material name is required.");

        AddRule(
            nameof(UpdateMaterialRequest.Name),
            r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length <= Material.NameMaxLength,
            $"Material name must be {Material.NameMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdateMaterialRequest.Code),
            r => string.IsNullOrWhiteSpace(r.Code) || r.Code.Trim().Length <= Material.CodeMaxLength,
            $"Code must be {Material.CodeMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdateMaterialRequest.Description),
            r => string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length <= Material.DescriptionMaxLength,
            $"Description must be {Material.DescriptionMaxLength} characters or fewer.");
    }
}

#endregion

#region VehicleType Validators

public sealed class CreateVehicleTypeRequestValidator : Validator<CreateVehicleTypeRequest>
{
    public CreateVehicleTypeRequestValidator()
    {
        AddRule(
            nameof(CreateVehicleTypeRequest.TypeName),
            r => !string.IsNullOrWhiteSpace(r.TypeName),
            "Vehicle type name is required.");

        AddRule(
            nameof(CreateVehicleTypeRequest.TypeName),
            r => string.IsNullOrWhiteSpace(r.TypeName) || r.TypeName.Trim().Length <= VehicleType.TypeNameMaxLength,
            $"Type name must be {VehicleType.TypeNameMaxLength} characters or fewer.");

        AddRule(
            nameof(CreateVehicleTypeRequest.Description),
            r => string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length <= VehicleType.DescriptionMaxLength,
            $"Description must be {VehicleType.DescriptionMaxLength} characters or fewer.");
    }
}

public sealed class UpdateVehicleTypeRequestValidator : Validator<UpdateVehicleTypeRequest>
{
    public UpdateVehicleTypeRequestValidator()
    {
        AddRule(
            nameof(UpdateVehicleTypeRequest.Id),
            r => r.Id > 0,
            "A valid vehicle type ID is required.");

        AddRule(
            nameof(UpdateVehicleTypeRequest.TypeName),
            r => !string.IsNullOrWhiteSpace(r.TypeName),
            "Vehicle type name is required.");

        AddRule(
            nameof(UpdateVehicleTypeRequest.TypeName),
            r => string.IsNullOrWhiteSpace(r.TypeName) || r.TypeName.Trim().Length <= VehicleType.TypeNameMaxLength,
            $"Type name must be {VehicleType.TypeNameMaxLength} characters or fewer.");

        AddRule(
            nameof(UpdateVehicleTypeRequest.Description),
            r => string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length <= VehicleType.DescriptionMaxLength,
            $"Description must be {VehicleType.DescriptionMaxLength} characters or fewer.");
    }
}

#endregion
