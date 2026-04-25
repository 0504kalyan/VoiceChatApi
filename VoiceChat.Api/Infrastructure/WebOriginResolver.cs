using Microsoft.AspNetCore.Http;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Resolves the SPA origin for redirects, reset links, and CORS without hardcoding localhost in production.
/// </summary>
public static class WebOriginResolver
{
    public static string ResolvePublicOrigin(HttpRequest request, string? configuredOrigin = null)
    {
        if (TryNormalizeOrigin(request.Headers.Origin, out var origin))
            return origin;

        if (TryNormalizeOriginFromReferer(request.Headers.Referer, out origin))
            return origin;

        if (TryNormalizeOrigin(configuredOrigin, out origin))
            return origin;

        throw new InvalidOperationException(
            "Could not determine the web client origin. Set WebClient:PublicOrigin in configuration or call the API from the browser so Origin/Referer headers are available.");
    }

    public static bool IsAllowedCorsOrigin(string origin, IEnumerable<string> configuredOrigins, string? configuredPublicOrigin)
    {
        if (!TryNormalizeOrigin(origin, out var normalized))
            return false;

        if (TryNormalizeOrigin(configuredPublicOrigin, out var configured) &&
            string.Equals(normalized, configured, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var candidate in configuredOrigins)
        {
            if (TryNormalizeOrigin(candidate, out var item) &&
                string.Equals(normalized, item, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var uri = new Uri(normalized);

        // Local dev and Vercel preview/prod are the only browser origins we expect by default.
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return true;

        return uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeOrigin(string? candidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    private static bool TryNormalizeOriginFromReferer(string? referer, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(referer))
            return false;

        if (!Uri.TryCreate(referer.Trim(), UriKind.Absolute, out var uri))
            return false;

        return TryNormalizeOrigin(uri.GetLeftPart(UriPartial.Authority), out normalized);
    }
}
