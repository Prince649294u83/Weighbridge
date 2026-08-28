using WeighBridge.Core.Application;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>
/// Covers the data-root resolution order in <see cref="ApplicationPaths"/>.
/// </summary>
/// <remarks>
/// This exists because of a data-loss defect rather than for completeness. The runtime smoke
/// scripts needed an empty database to test a cold start, had no way to relocate the data root
/// from outside the process, and so deleted the database at the real path — which destroyed a
/// live installation's weighment history. The environment variable is what lets them use a
/// temporary root instead, so if it stops being honoured the scripts quietly go back to
/// writing to the operator's installation.
/// </remarks>
public sealed class ApplicationPathsTests : IDisposable
{
    private readonly string? _originalValue
        = Environment.GetEnvironmentVariable(ApplicationPaths.DataRootVariable);

    [Fact]
    public void DataRoot_DefaultsToLocalApplicationData_WhenNothingOverridesIt()
    {
        Environment.SetEnvironmentVariable(ApplicationPaths.DataRootVariable, null);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationPaths.ProductFolderName);

        Assert.Equal(expected, new ApplicationPaths().DataRoot);
    }

    [Fact]
    public void DataRoot_ComesFromTheEnvironment_WhenNoExplicitRootIsGiven()
    {
        var isolated = Path.Combine(Path.GetTempPath(), "WeighBridge.PathsTest", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(ApplicationPaths.DataRootVariable, isolated);

        var paths = new ApplicationPaths();

        // Every derived location has to move with the root, not just the root itself: a script
        // that relocated the database but still wrote captures into the live installation
        // would be isolated in name only.
        Assert.Equal(Path.GetFullPath(isolated), paths.DataRoot);
        Assert.StartsWith(paths.DataRoot, paths.DatabaseDirectory, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataRoot, paths.LogsDirectory, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataRoot, paths.CaptureDirectory, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataRoot, paths.ReportsDirectory, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataRoot, paths.ConfigurationFile, StringComparison.Ordinal);
        Assert.StartsWith(paths.DataRoot, paths.UserPreferencesFile, StringComparison.Ordinal);
    }

    [Fact]
    public void DataRoot_PrefersTheExplicitRoot_OverTheEnvironment()
    {
        Environment.SetEnvironmentVariable(
            ApplicationPaths.DataRootVariable,
            Path.Combine(Path.GetTempPath(), "WeighBridge.PathsTest", "from-environment"));

        var explicitRoot = Path.Combine(Path.GetTempPath(), "WeighBridge.PathsTest", "explicit");

        Assert.Equal(Path.GetFullPath(explicitRoot), new ApplicationPaths(explicitRoot).DataRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DataRoot_IgnoresABlankEnvironmentValue(string blank)
    {
        Environment.SetEnvironmentVariable(ApplicationPaths.DataRootVariable, blank);

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationPaths.ProductFolderName);

        // An unset variable and one set to whitespace have to behave the same way. A script
        // that exported an empty value would otherwise root the whole application at the
        // process working directory.
        Assert.Equal(expected, new ApplicationPaths().DataRoot);
    }

    public void Dispose()
        => Environment.SetEnvironmentVariable(ApplicationPaths.DataRootVariable, _originalValue);
}
