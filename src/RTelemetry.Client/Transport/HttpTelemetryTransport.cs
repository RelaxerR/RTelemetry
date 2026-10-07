using System.Net;
using System.Net.Http.Json;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Transport;

/// <summary>Отправка пачек POST-запросом на <c>{endpoint}/v1/batches</c>.</summary>
public sealed class HttpTelemetryTransport : ITelemetryTransport
{
    private readonly HttpClient _http;
    private readonly Uri _batchesUri;
    private readonly string _apiKey;

    public HttpTelemetryTransport(HttpClient http, Uri endpoint, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;

        // Без завершающего слэша относительный путь заменил бы последний сегмент базового адреса.
        var baseUri = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
        _batchesUri = new Uri(baseUri, TelemetryProtocol.BatchesPath);
    }

    public async Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _batchesUri)
        {
            Content = JsonContent.Create(batch, options: TelemetryProtocol.JsonOptions),
        };
        request.Headers.Add(TelemetryProtocol.ApiKeyHeader, _apiKey);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return SendResult.Accepted;
            }

            var code = (int)response.StatusCode;
            return code >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
                ? SendResult.RetryLater
                : SendResult.Rejected;
        }
        catch (HttpRequestException)
        {
            return SendResult.RetryLater;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут HttpClient, а не отмена вызывающим.
            return SendResult.RetryLater;
        }
    }
}
