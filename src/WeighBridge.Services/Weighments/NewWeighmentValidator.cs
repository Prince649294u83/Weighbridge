using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Validation;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Services.Weighments;

/// <summary>
/// Checks what an operator typed before a weighment is opened.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is the operator's to fix, which is what separates it from the invariants
/// <see cref="Weighment"/> enforces by throwing. A blank vehicle number is a mistake to point
/// at a field about; a second weight with no first is a record that must not exist. Only the
/// first kind belongs in a validator.
/// </para>
/// <para>
/// The length rules test the <em>normalised</em> vehicle number, because that is the value
/// that reaches the column. Testing what was typed would accept a registration that then
/// failed at the database with a message no operator can act on.
/// </para>
/// </remarks>
public sealed class NewWeighmentValidator : Validator<NewWeighment>
{
    /// <summary>Declares the rules.</summary>
    public NewWeighmentValidator()
    {
        AddRule(
            nameof(NewWeighment.VehicleNumber),
            request => !string.IsNullOrWhiteSpace(request.VehicleNumber),
            "Enter the vehicle number.");

        AddRule(
            nameof(NewWeighment.VehicleNumber),
            request => string.IsNullOrWhiteSpace(request.VehicleNumber)
                || Weighment.NormaliseVehicleNumber(request.VehicleNumber).Length > 0,
            "A vehicle number needs at least one letter or digit.");

        AddRule(
            nameof(NewWeighment.VehicleNumber),
            request => string.IsNullOrWhiteSpace(request.VehicleNumber)
                || Weighment.NormaliseVehicleNumber(request.VehicleNumber).Length
                    <= Weighment.VehicleNumberMaxLength,
            $"A vehicle number cannot be longer than {Weighment.VehicleNumberMaxLength} characters.");

        AddRule(
            nameof(NewWeighment.Mode),
            request => Enum.IsDefined(request.Mode),
            "Choose whether the vehicle arrived loaded or empty.");

        AddLengthRule(nameof(NewWeighment.PartyName), request => request.PartyName, Weighment.NameMaxLength);
        AddLengthRule(nameof(NewWeighment.MaterialName), request => request.MaterialName, Weighment.NameMaxLength);
        AddLengthRule(nameof(NewWeighment.DriverName), request => request.DriverName, Weighment.NameMaxLength);
        AddLengthRule(nameof(NewWeighment.TransporterName), request => request.TransporterName, Weighment.NameMaxLength);
        AddLengthRule(nameof(NewWeighment.Remarks), request => request.Remarks, Weighment.TextMaxLength);
    }

    private void AddLengthRule(string propertyName, Func<NewWeighment, string?> value, int maximum)
        => AddRule(
            propertyName,
            request => (value(request)?.Trim().Length ?? 0) <= maximum,
            $"Keep this to {maximum} characters or fewer.");
}
