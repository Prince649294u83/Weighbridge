using Microsoft.Extensions.Options;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Produces the SQLite connection string from configuration, falling back to a file
/// inside the application data root when no explicit string is supplied.
/// </summary>
public sealed class ConnectionStringProvider(IOptions<DatabaseOptions> options, IApplicationPaths paths)
{
    private readonly DatabaseOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IApplicationPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>Full path of the SQLite database file.</summary>
    public string DatabaseFilePath
    {
        get
        {
            var fileName = string.IsNullOrWhiteSpace(_options.FileName)
                ? "weighbridge.db"
                : _options.FileName;

            return Path.IsPathRooted(fileName)
                ? fileName
                : Path.Combine(_paths.DatabaseDirectory, fileName);
        }
    }

    /// <summary>The connection string handed to the SQLite provider.</summary>
    public string ConnectionString => string.IsNullOrWhiteSpace(_options.ConnectionString)
        ? $"Data Source={DatabaseFilePath};Mode=ReadWriteCreate;Default Timeout=30;"
        : _options.ConnectionString;

    /// <summary>Short description shown in the status bar tooltip.</summary>
    public string Description => $"{_options.Provider} · {Path.GetFileName(DatabaseFilePath)}";
}
