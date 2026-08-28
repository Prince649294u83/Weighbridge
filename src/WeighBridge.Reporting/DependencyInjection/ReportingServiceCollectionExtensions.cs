using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Abstractions;
using WeighBridge.Reporting.Services;

namespace WeighBridge.Reporting.DependencyInjection;

/// <summary>Registers the reporting layer.</summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>Adds the CSV report service.</summary>
    public static IServiceCollection AddWeighBridgeReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReportService, CsvReportService>();

        return services;
    }
}
