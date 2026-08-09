using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Logging;

/// <summary>Registration helpers for the daily file logger.</summary>
public static class FileLoggerExtensions
{
    /// <summary>
    /// Adds the rolling file logger, writing into <paramref name="logDirectory"/>.
    /// </summary>
    /// <remarks>
    /// The provider instance is also registered as a singleton so the shell can show
    /// the active log file path and flush on shutdown.
    /// </remarks>
    public static ILoggingBuilder AddWeighBridgeFileLogger(
        this ILoggingBuilder builder,
        string logDirectory,
        FileLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return builder;
        }

        var provider = new FileLoggerProvider(logDirectory, options);

        builder.AddProvider(provider);
        builder.Services.AddSingleton(provider);

        return builder;
    }
}
