using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Messaging;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// SMS delivery provider transmitting via a remote REST / HTTP Gateway with header-level secret isolation.
/// </summary>
public sealed class HttpGatewaySmsProvider : ISmsProvider
{
    public const string ProviderName = "HttpGateway";

    private readonly HttpClient _httpClient;
    private readonly IOptions<SmsOptions> _options;
    private readonly ILogger<HttpGatewaySmsProvider> _logger;

    public HttpGatewaySmsProvider(
        HttpClient httpClient,
        IOptions<SmsOptions> options,
        ILogger<HttpGatewaySmsProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<SmsSendResult> SendAsync(
        string recipientPhoneNumber,
        string messageContent,
        CancellationToken cancellationToken = default)
    {
        var opt = _options.Value;

        if (string.IsNullOrWhiteSpace(opt.HttpEndpointUrl))
        {
            return SmsSendResult.ConfigurationFailure("HTTP Gateway endpoint URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(recipientPhoneNumber))
        {
            return SmsSendResult.PermanentFailure("Recipient phone number is missing or empty.");
        }

        try
        {
            var payload = new
            {
                recipient = recipientPhoneNumber,
                message = messageContent,
                timestamp = DateTime.UtcNow
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, opt.HttpEndpointUrl)
            {
                Content = jsonContent
            };

            // Secret isolation: Inject API key strictly at the request header level, NEVER in the URL
            if (!string.IsNullOrWhiteSpace(opt.HttpApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.HttpApiKey);
                request.Headers.TryAddWithoutValidation("X-Api-Key", opt.HttpApiKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("SMS successfully delivered via HTTP Gateway to {Recipient}", recipientPhoneNumber);
                return SmsSendResult.Success(responseBody);
            }

            var statusCode = response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogWarning("HTTP Gateway returned status {StatusCode} for {Recipient}: {ErrorBody}",
                statusCode, recipientPhoneNumber, errorBody);

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                return SmsSendResult.ConfigurationFailure($"HTTP Gateway Authentication failed ({statusCode}).");
            }

            if (statusCode == HttpStatusCode.BadRequest || statusCode == HttpStatusCode.UnprocessableEntity)
            {
                return SmsSendResult.PermanentFailure($"HTTP Gateway rejected request ({statusCode}): {errorBody}");
            }

            // 429 Too Many Requests or 5xx Server Errors are transient
            return SmsSendResult.TransientFailure($"HTTP Gateway server error ({statusCode}): {errorBody}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error communicating with HTTP SMS Gateway");
            return SmsSendResult.TransientFailure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception during HTTP SMS transmission");
            return SmsSendResult.TransientFailure($"HTTP send failure: {ex.Message}");
        }
    }
}
