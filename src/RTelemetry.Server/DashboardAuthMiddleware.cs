using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace RTelemetry.Server;

/// <summary>
/// HTTP Basic для фронта статистики (всё, кроме <c>/v1/*</c> и <c>/health</c>).
/// Пустой пароль в настройках — фронт выключен и отвечает 404. Использовать только за HTTPS.
/// </summary>
public sealed class DashboardAuthMiddleware(RequestDelegate next, IOptionsMonitor<TelemetryServerOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/v1") || path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var dashboard = options.CurrentValue.Dashboard;
        if (string.IsNullOrEmpty(dashboard.Password))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (IsAuthorized(context.Request.Headers.Authorization.ToString(), dashboard))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"RTelemetry\", charset=\"UTF-8\"";
    }

    private static bool IsAuthorized(string header, DashboardOptions dashboard)
    {
        const string prefix = "Basic ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[prefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = $"{dashboard.User}:{dashboard.Password}";
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(decoded), Encoding.UTF8.GetBytes(expected));
    }
}
