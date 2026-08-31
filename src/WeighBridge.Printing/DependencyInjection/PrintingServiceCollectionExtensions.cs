using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Abstractions;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Services;
using WeighBridge.Printing.Template;

namespace WeighBridge.Printing.DependencyInjection;

/// <summary>Registers the printing layer.</summary>
public static class PrintingServiceCollectionExtensions
{
    /// <summary>Adds the print service, template engine, and output drivers.</summary>
    public static IServiceCollection AddWeighBridgePrinting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITemplateEngine, SlipTemplateEngine>();
        services.AddSingleton<WindowsGdiPrintOutput>();
        services.AddSingleton<RawSpoolPrintOutput>();
        services.AddSingleton<IPrintService, WindowsPrintService>();

        return services;
    }
}
