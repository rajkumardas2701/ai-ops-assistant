using Microsoft.AspNetCore.Http;

namespace AiOps.Api.Functions;

/// <summary>
/// Resolves the tenant for a request. Multi-tenancy is header-driven: the gateway (or client)
/// supplies <c>X-Tenant-Id</c>; a <c>?tenant=</c> query value is accepted as a fallback for easy
/// manual testing. Absent both, the request maps to the shared <c>default</c> tenant so the
/// existing single-tenant UX keeps working unchanged.
/// </summary>
public static class TenantResolver
{
    public const string DefaultTenant = "default";
    private const string HeaderName = "X-Tenant-Id";

    public static string Resolve(HttpRequest req)
    {
        if (req.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header))
            return Normalize(header.ToString());

        var query = req.Query["tenant"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(query))
            return Normalize(query);

        return DefaultTenant;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
