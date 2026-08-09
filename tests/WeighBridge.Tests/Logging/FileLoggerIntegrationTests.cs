using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Logging;

/// <summary>
/// Runs a category logger through the real <see cref="FileLoggerProvider"/> and reads the
/// file back.
/// </summary>
/// <remarks>
/// The other logging tests use a recording sink, which proves the enrichment is produced
/// but not that it survives the trip to disk. This one covers the seam between
/// <see cref="LogEnrichment"/> and the file logger's scope rendering — the part that would
/// silently degrade to an empty <c>{}</c> if the two ever stopped agreeing.
/// </remarks>
public sealed class FileLoggerIntegrationTests
{
    private static async Task<string> WriteAndReadAsync(Action<ApplicationLogger> write)
    {
        using var dataRoot = new TempDataRoot();
        dataRoot.Paths.EnsureCreated();

        var options = new FileLoggingOptions
        {
            Enabled = true,
            MinimumLevel = LogLevel.Trace,
            IncludeDebugOutput = false,
        };

        var provider = new FileLoggerProvider(dataRoot.Paths.LogsDirectory, options);

        try
        {
            using var factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(provider);
            });

            write(new ApplicationLogger(factory, new TestApplicationInfoService()));

            provider.Flush();

            // The writer drains on its own thread; give it a moment beyond the flush.
            await Task.Delay(150);

            var path = provider.CurrentLogFilePath;
            Assert.NotNull(path);

            return await File.ReadAllTextAsync(path);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Fact]
    public async Task EnrichmentReachesTheFileAsScopeText()
    {
        var contents = await WriteAndReadAsync(logger =>
        {
            using (logger.BeginOperation("VehicleEntry", "corr-file"))
            {
                logger.Information("Ticket {Ticket} saved", "T-9001");
            }
        });

        Assert.Contains("module=VehicleEntry", contents);
        Assert.Contains("corr=corr-file", contents);
        Assert.Contains("user=testoperator", contents);
        Assert.Contains("machine=TESTRIG", contents);
        Assert.Contains("version=9.9.9", contents);

        // The scope is rendered between braces, as the existing log format specifies.
        Assert.Contains("{module=VehicleEntry", contents);

        Assert.Contains("Ticket T-9001 saved", contents);
        Assert.Contains(LogCategory.Application, contents);
        Assert.Contains("[INF]", contents);
    }

    [Fact]
    public async Task ExceptionAndInnerExceptionReachTheFile()
    {
        var contents = await WriteAndReadAsync(logger =>
        {
            var inner = new TimeoutException("no response on COM3");
            var outer = new InvalidOperationException("weight capture failed", inner);

            logger.Error(outer, "Could not read the indicator");
        });

        Assert.Contains("[ERR]", contents);
        Assert.Contains("Could not read the indicator", contents);
        Assert.Contains("InvalidOperationException", contents);

        // The inner exception and a stack trace must both survive: they are the only
        // evidence available when a site reports a fault days later.
        Assert.Contains("TimeoutException", contents);
        Assert.Contains("no response on COM3", contents);
    }
}
