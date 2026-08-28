using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Abstractions;
using WeighBridge.Printing.Services;

namespace WeighBridge.Printing.DependencyInjection;

/// <summary>Registers the printing layer.</summary>
public static class PrintingServiceCollectionExtensions
{
    /// <summary>Adds the placeholder print service.</summary>
    public static IServiceCollection AddWeighBridgePrinting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPrintService, WindowsPrintService>();

        return services;
    }
}
