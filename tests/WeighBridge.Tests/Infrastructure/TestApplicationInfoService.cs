using WeighBridge.Core.Application;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>
/// <see cref="IApplicationInfoService"/> with fixed values, so a test can assert on the
/// exact enrichment text rather than on whatever machine it happens to run on.
/// </summary>
internal sealed class TestApplicationInfoService : IApplicationInfoService
{
    public string ApplicationName => "WeighBridge Modern";

    public string Version => "9.9.9";

    public string DisplayVersion => "v9.9.9";

    public DateTime BuildDate => new(2026, 1, 1);

    public string OperatingSystem => "Test OS";

    public string MachineName => "TESTRIG";

    public string CurrentUserName => "testoperator";
}
