using System.Text.RegularExpressions;
using WeighBridge.Core.Printing;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// Authoritative SMS template engine rendering 9-field legacy template parity from canonical <see cref="WeighmentPrintData"/>.
/// </summary>
public sealed class SmsTemplateEngine
{
    public const string DefaultTemplate =
@"Ticket No %%|ticketno^{""inputtype"" : ""text"", ""maxlength"" : ""20""}%%
Vehicle No %%|vehicleno^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
Party %%|party^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
Item %%|item^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
Charges %%|charges^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
VehicalType %%|vehicaltype^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
G WT%%|Gwt^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
T WT %%|Twt^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%
Net WT %%|Nettwt^{""inputtype"" : ""text"", ""maxlength"" : ""50""}%%";

    // Matches %%|token^...%% legacy token format
    private static readonly Regex TokenPattern = new(
        @"%%\|(?<token>[a-zA-Z0-9_]+)(\^[^%]*)?%%",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Renders an SMS message string from the provided template and canonical print data.
    /// </summary>
    public string Render(string? template, WeighmentPrintData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var tmpl = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;

        return TokenPattern.Replace(tmpl, match =>
        {
            var token = match.Groups["token"].Value;
            return ResolveToken(token, data);
        });
    }

    private static string ResolveToken(string token, WeighmentPrintData data)
    {
        return token.ToLowerInvariant() switch
        {
            "ticketno" or "ticket" or "slipnumber" => string.IsNullOrWhiteSpace(data.SlipNumber) ? "-" : data.SlipNumber,
            "vehicleno" or "vehicle" or "vehiclenumber" => string.IsNullOrWhiteSpace(data.VehicleNumber) ? "-" : data.VehicleNumber,
            "party" or "partyname" => string.IsNullOrWhiteSpace(data.PartyName) ? "-" : data.PartyName,
            "item" or "material" or "materialname" => string.IsNullOrWhiteSpace(data.MaterialName) ? "-" : data.MaterialName,
            "charges" or "totalcharges" => data.TotalCharges.ToString("0.00"),
            "vehicaltype" or "vehicletype" or "vtype" => string.IsNullOrWhiteSpace(data.VehicleTypeName) ? "-" : data.VehicleTypeName,
            "gwt" or "gross" or "grossweight" => data.GrossWeightKg > 0 ? data.GrossWeightKg.ToString("F1") : "-",
            "twt" or "tare" or "tareweight" => data.TareWeightKg > 0 ? data.TareWeightKg.ToString("F1") : "-",
            "nettwt" or "net" or "netweight" => data.NetWeightKg > 0 ? data.NetWeightKg.ToString("F1") : "-",
            _ => "-"
        };
    }
}
