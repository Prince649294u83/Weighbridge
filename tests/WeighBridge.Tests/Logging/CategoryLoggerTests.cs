using Microsoft.Extensions.Logging;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Logging;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Logging;

/// <summary>
/// Proves the enrichment reaches the sink and that the ambient scopes behave across
/// nesting and across an await.
/// </summary>
public sealed class CategoryLoggerTests
{
    private readonly RecordingLoggerProvider _sink = new();
    private readonly ILoggerFactory _factory;

    public CategoryLoggerTests()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(_sink);
        });
    }

    private ApplicationLogger CreateApplicationLogger()
        => new(_factory, new TestApplicationInfoService());

    private AuditLogger CreateAuditLogger()
        => new(_factory, new TestApplicationInfoService());

    [Fact]
    public void Information_WritesUnderTheApplicationCategory()
    {
        CreateApplicationLogger().Information("Started");

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(LogCategory.Application, entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Started", entry.Message);
    }

    [Fact]
    public void EveryEntry_CarriesUserMachineAndVersion()
    {
        CreateApplicationLogger().Information("Started");

        var scope = Assert.Single(_sink.Entries).ScopeText;
        Assert.Contains("user=testoperator", scope);
        Assert.Contains("machine=TESTRIG", scope);
        Assert.Contains("version=9.9.9", scope);
    }

    [Fact]
    public void Entry_CarriesTheAmbientModule()
    {
        var logger = CreateApplicationLogger();

        using (ModuleScope.Begin("VehicleEntry"))
        {
            logger.Information("Saved");
        }

        Assert.Contains("module=VehicleEntry", Assert.Single(_sink.Entries).ScopeText);
    }

    [Fact]
    public void Entry_OutsideAModuleScope_OmitsTheModuleField()
    {
        CreateApplicationLogger().Information("Saved");

        Assert.DoesNotContain("module=", Assert.Single(_sink.Entries).ScopeText);
    }

    [Fact]
    public void Entry_CarriesTheAmbientCorrelationId()
    {
        var logger = CreateApplicationLogger();

        using (CorrelationScope.Begin("abc123"))
        {
            logger.Information("Saved");
        }

        Assert.Contains("corr=abc123", Assert.Single(_sink.Entries).ScopeText);
    }

    [Fact]
    public void BeginOperation_TagsModuleAndCorrelationTogether()
    {
        var logger = CreateApplicationLogger();

        using (logger.BeginOperation("Printing", "corr-1"))
        {
            logger.Information("Printed");
        }

        var scope = Assert.Single(_sink.Entries).ScopeText;
        Assert.Contains("module=Printing", scope);
        Assert.Contains("corr=corr-1", scope);
    }

    [Fact]
    public void BeginOperation_RestoresTheEnclosingScopeOnDispose()
    {
        var logger = CreateApplicationLogger();

        using (logger.BeginOperation("Outer", "corr-outer"))
        {
            using (logger.BeginOperation("Inner", "corr-inner"))
            {
                logger.Information("inner");
            }

            logger.Information("outer");
        }

        Assert.Contains("module=Inner", _sink.Entries[0].ScopeText);
        Assert.Contains("corr=corr-inner", _sink.Entries[0].ScopeText);
        Assert.Contains("module=Outer", _sink.Entries[1].ScopeText);
        Assert.Contains("corr=corr-outer", _sink.Entries[1].ScopeText);
    }

    [Fact]
    public async Task OperationScope_FlowsAcrossAnAwait()
    {
        var logger = CreateApplicationLogger();

        using (logger.BeginOperation("Weighment", "corr-async"))
        {
            await Task.Yield();
            await Task.Delay(1);

            logger.Information("after the await");
        }

        var scope = Assert.Single(_sink.Entries).ScopeText;
        Assert.Contains("module=Weighment", scope);
        Assert.Contains("corr=corr-async", scope);
    }

    [Fact]
    public void NestedCorrelation_WithoutAnId_InheritsTheEnclosingOne()
    {
        var logger = CreateApplicationLogger();

        using (logger.BeginOperation("Outer", "corr-parent"))
        using (logger.BeginOperation("Inner"))
        {
            logger.Information("nested");
        }

        Assert.Contains("corr=corr-parent", Assert.Single(_sink.Entries).ScopeText);
    }

    [Fact]
    public void Error_RecordsTheExceptionRatherThanFormattingItIntoTheMessage()
    {
        var failure = new InvalidOperationException("indicator timed out");

        CreateApplicationLogger().Error(failure, "Weight capture failed");

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Weight capture failed", entry.Message);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public void Error_PreservesTheInnerException()
    {
        var inner = new TimeoutException("no response on COM3");
        var outer = new InvalidOperationException("capture failed", inner);

        CreateApplicationLogger().Error(outer, "Weight capture failed");

        Assert.Same(inner, Assert.Single(_sink.Entries).Exception?.InnerException);
    }

    [Fact]
    public void MessageTemplate_SubstitutesItsArguments()
    {
        CreateApplicationLogger().Warning("Port {Port} unreachable after {Attempts} attempts", "COM3", 3);

        Assert.Equal("Port COM3 unreachable after 3 attempts", Assert.Single(_sink.Entries).Message);
    }

    [Fact]
    public void DisabledLevel_WritesNothing()
    {
        _sink.MinimumLevel = LogLevel.Warning;

        var logger = CreateApplicationLogger();
        logger.Debug("noise");
        logger.Trace("more noise");

        Assert.Empty(_sink.Entries);
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void EachLogger_UsesItsOwnCategory()
    {
        var info = new TestApplicationInfoService();

        new HardwareLogger(_factory, info).Information("indicator");
        new DatabaseLogger(_factory, info).Information("query");
        new UIInteractionLogger(_factory, info).Navigated("Dashboard", "Reports");
        CreateAuditLogger().Record("Created", "Ticket", "T-1");

        Assert.Equal(LogCategory.Hardware, _sink.Entries[0].Category);
        Assert.Equal(LogCategory.Database, _sink.Entries[1].Category);
        Assert.Equal(LogCategory.UserInterface, _sink.Entries[2].Category);
        Assert.Equal(LogCategory.Audit, _sink.Entries[3].Category);
    }

    [Fact]
    public void Audit_RecordsActionEntityAndIdentity()
    {
        CreateAuditLogger().Record("Updated", "Ticket", "T-4471", "gross 18420 -> 18460");

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Updated", entry.Message);
        Assert.Contains("Ticket", entry.Message);
        Assert.Contains("T-4471", entry.Message);
        Assert.Contains("gross 18420 -> 18460", entry.Message);
        Assert.Contains("user=testoperator", entry.ScopeText);
    }

    [Fact]
    public void Audit_RecordsARefusalAsAWarning()
    {
        CreateAuditLogger().RecordDenied("Delete", "Ticket", "already printed", "T-4471");

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("DENIED", entry.Message);
        Assert.Contains("already printed", entry.Message);
    }

    [Fact]
    public void Audit_RejectsAnEmptyAction()
    {
        var logger = CreateAuditLogger();

        Assert.Throws<ArgumentException>(() => logger.Record(" ", "Ticket"));
        Assert.Throws<ArgumentException>(() => logger.Record("Created", " "));
    }

    [Fact]
    public void UiInteractionLogger_WritesNavigationAtDebugLevel()
    {
        new UIInteractionLogger(_factory, new TestApplicationInfoService())
            .Navigated("Dashboard", "Reports");

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("Navigated from Dashboard to Reports", entry.Message);
    }

    [Fact]
    public void ModuleScope_RejectsABlankModule()
        => Assert.Throws<ArgumentException>(() => ModuleScope.Begin(" "));

    [Fact]
    public void OperationScope_RejectingItsModule_DoesNotLeakTheCorrelationScope()
    {
        Assert.Throws<ArgumentException>(() => OperationScope.Begin(" ", "corr-leak"));

        // The correlation scope opened first must have been unwound, otherwise every
        // later entry in this test run would be tagged corr-leak.
        Assert.Null(CorrelationScope.Current);
    }
}
