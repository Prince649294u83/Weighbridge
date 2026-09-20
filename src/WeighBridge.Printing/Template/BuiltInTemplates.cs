namespace WeighBridge.Printing.Template;

/// <summary>
/// Authoritative built-in template constants providing legacy parity out of the box.
/// </summary>
public static class BuiltInTemplates
{
    /// <summary>Standard 80-column plain text / dot-matrix slip (Print_Ticket.txt).</summary>
    public const string Standard =
@"<Start>
<Header><Nameset><Name></Nameset>
<Address1>
<Address2></Header>
<->
<SlipType>
<->
Slip No  : <ticket>                       Date : <date>
Party    : <party>
Vehicle  : <vehicle>                      Type : <vtype>
Material : <item>                         Charges: Rs. <charges>
<->
Gross Wt : <gweight> kg        Date: <gdate>  Time: <gtime>
Tare Wt  : <tweight> kg        Date: <tdate>  Time: <ttime>
Net Wt   : <nweight> kg
<->
Operator : <username>

Signature: __________________          Driver Signature: __________________
<->";

    /// <summary>Advanced slip with bags, tare deduction, gate pass and actual weight (Print_Ticket_Advanced.txt).</summary>
    public const string Advanced =
@"<Start>
<Header><Nameset><Name></Nameset>
<Address1>
<Address2></Header>
<->
<SlipType>
<->
Slip No   : <ticket>                      Date : <date>
Party     : <party>
Vehicle   : <vehicle>                     Type : <vtype>
Material  : <item>                        Gate Pass : <gatepass>
Charges   : Rs. <charges>
<->
Gross Wt  : <gweight> kg       Date: <gdate>  Time: <gtime>
Tare Wt   : <tweight> kg       Date: <tdate>  Time: <ttime>
Net Wt    : <nweight> kg
Bags      : <bags>                     Empty Bag Wt : <emptybagwt> kg
Total Bag : <totalbagweight> kg        Actual Wt    : <acualwt> kg
<->
Operator  : <username>

Signature : __________________         Driver Signature: __________________
<->";

    /// <summary>Food Corporation of India format slip (Print_Ticket_FCI.txt).</summary>
    public const string FCI =
@"<Start>
<Header><Nameset><Name></Nameset>
<Address1>
<Address2></Header>
<->
<SlipType> (FCI FORMAT)
<->
Slip No   : <ticket>                      Date : <date>
Consigner : <field1>                      Party: <party>
Vehicle   : <vehicle>                     Type : <vtype>
Grain     : <item>                        Bags : <field2>
Bag Wt    : <field3> kg                   Charges: Rs. <charges>
<->
Gross Wt  : <gweight> kg       Date: <gdate>  Time: <gtime>
Tare Wt   : <tweight> kg       Date: <tdate>  Time: <ttime>
Net Wt    : <nweight> kg       Actual Wt : <field4> kg
<->
Operator  : <username>

Authorized Signature: _________________    Driver: _________________
<->";

    /// <summary>3-inch (80mm) Thermal POS receipt with ESC/POS cut (Print_Ticket_Thermal.txt).</summary>
    public const string Thermal = "<Start>\n<Bold><Name></Bold>\n<Address1>\n<Address2>\n<->\n<Bold><SlipType></Bold>\n<->\nSlip: <ticket>     Date: <date>\nParty: <party>\nVeh  : <vehicle> (<vtype>)\nMat  : <item>\n<->\nGross: <gweight> kg  <gtime>\nTare : <tweight> kg  <ttime>\n<Bold>Net  : <nweight> kg</Bold>\nCharges: Rs. <charges>\n<->\nOp: <username>\n<Cut>";

    /// <summary>Continuous tractor-feed Dot Matrix slip matching physical legacy hardware (Print_Ticket_Dot.txt).</summary>
    public const string DotMatrix =
@"<Start>
<Header><Nameset><Name></Nameset>
<Address1>
<Address2></Header>

        Date : <date>
    Serial No.   : <ticket,15>       Party Name   : <party>
    Vehicle No   : <vehicle,15>      Material     : <item>
    Vehicle Type : <vtype,15>        Charges      : Rs. <charges,0>
    -----------------------------------------------------------------
    Gross Weight  : <gweight,12>     Kg.   <gdatetime>

    Tare Weight   : <tweight,12>     Kg.   <tdatetime>

    Net Weight    : <nweight,12>     Kg.

                                                        Operator Sign.
                                               User Name : <username>
    -----------------------------------------------------------------
<->";

    /// <summary>A4 full-page formatted slip (Print_Ticket_A4.txt).</summary>
    public const string A4 = Advanced;

    /// <summary>
    /// Retrieves a built-in template by name or returns Standard as fallback.
    /// </summary>
    public static string GetByName(string? templateName)
    {
        return (templateName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "advanced" => Advanced,
            "fci" => FCI,
            "thermal" => Thermal,
            "dot" or "dotmatrix" or "genericascii" or "tractor" => DotMatrix,
            "a4" => A4,
            _ => Standard
        };
    }
}
