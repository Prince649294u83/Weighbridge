namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a setting changed at runtime.
/// </summary>
/// <remarks>
/// Lets a module react to a configuration change without polling and without holding a
/// reference to the settings screen. <see cref="SectionName"/> is coarse on purpose: a
/// subscriber filters on the section it cares about and ignores the rest.
/// </remarks>
public sealed class SettingsChangedEvent : ApplicationEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="sectionName">Configuration section that changed, e.g. <c>Printer</c>.</param>
    /// <param name="settingName">Specific setting, when the change was to a single value.</param>
    /// <param name="source">Component that raised the event.</param>
    public SettingsChangedEvent(string sectionName, string? settingName = null, string? source = null)
        : base(source)
    {
        SectionName = sectionName;
        SettingName = settingName;
    }

    /// <summary>Configuration section that changed.</summary>
    public string SectionName { get; }

    /// <summary>
    /// The individual setting, or <c>null</c> when the whole section was replaced.
    /// </summary>
    public string? SettingName { get; }
}
