using System.Net;
using System.Net.Http.Json;

namespace RTelemetry.Server;

internal sealed class InstallAuthorization(HttpClient http)
{
    public async Task<int> CheckAsync(ProjectOptions project, Guid installId, CancellationToken cancellationToken)
    {
        if (project.InstallAuthorizationUrl.Length == 0 && !project.RequireInstallAuthorization)
            return StatusCodes.Status204NoContent;

        using var request = new HttpRequestMessage(HttpMethod.Post, project.InstallAuthorizationUrl)
        {
            Content = JsonContent.Create(new { telemetryInstallId = installId }),
        };
        request.Headers.Add("X-Research-Authorization-Key", project.InstallAuthorizationKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => StatusCodes.Status204NoContent,
                HttpStatusCode.Forbidden => StatusCodes.Status410Gone,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return StatusCodes.Status503ServiceUnavailable;
        }
    }
}
