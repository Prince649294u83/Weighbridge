namespace WeighBridge.Domain.Enums;

/// <summary>
/// Which of the two weights is the gross one.
/// </summary>
/// <remarks>
/// <para>
/// This is captured when the weighment is opened because only the operator knows it: a
/// loaded vehicle arriving to tip weighs gross first and tare after unloading, while an
/// empty vehicle arriving to load weighs tare first and gross after loading.
/// </para>
/// <para>
/// The alternative — calling the heavier of the two readings the gross — would make a net
/// weight arithmetically impossible to get wrong and therefore impossible to detect when
/// it is wrong. Recording the intent instead means a tare heavier than its gross is a
/// rejected weighment rather than a silently inverted one.
/// </para>
/// </remarks>
public enum WeighmentMode
{
    /// <summary>Vehicle arrives loaded: first weight is gross, second is tare.</summary>
    GrossFirst = 0,

    /// <summary>Vehicle arrives empty: first weight is tare, second is gross.</summary>
    TareFirst = 1,
}
