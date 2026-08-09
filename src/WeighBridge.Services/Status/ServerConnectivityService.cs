using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Services.Status;

/// <summary>
/// Probes the optional central server with a lightweight HTTP request.
/// </summary>
/// <remarks>
/// A standalone weighbridge has no server, which is why the default configuration
/// disables this check and the status bar reports "Disabled" rather than an error.
/// </remarks>
public sealed class ServerConnectivityService(
    IOptions<ServerOptions> options,
    ILogger<ServerConnectivityService> logger) : IServerConnectivityService, IDisposable
{
    private readonly ServerOptions _options = options.Value;
    private readonly ILogger<ServerConnectivityService> _logger = logger;
    private readonly Lazy<HttpClient> _client = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(Math.Max(5, options.Value.TimeoutSeconds)),
    });

    /// <inheritdoc />
    public string Name => "Server";

    /// <inheritdoc />
    public string EndpointDescription => _options.BaseUrl;

    /// <inheritdoc />
    public async Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return HealthResult.Disabled("Server sync disabled in configuration");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri))
        {
            return HealthResult.Unreachable("No valid server address configured");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await _client.Value
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthResult.Healthy(uri.Host)
                : HealthResult.Degraded($"{uri.Host} returned {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return HealthResult.Unreachable($"{uri.Host} timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Server health check failed for {Endpoint}", uri);
            return HealthResult.Unreachable($"{uri.Host} unreachable");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
