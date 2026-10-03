using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Wbskt.Infrastructure.Configuration;

public static class ApiDocsConfiguration
{
    /// <summary>
    /// Whether a host maps its OpenAPI document (<c>/openapi/v1.json</c>) and the Scalar reference
    /// (<c>/scalar</c>). Read from <c>ApiDocs:Enabled</c> - <c>API_DOCS_ENABLED</c> in the compose
    /// .env - and on by default only in Development. Both endpoints are anonymous, so turning this
    /// on in a deployed environment publishes the full API surface to anyone who finds the URL.
    /// </summary>
    public static bool IsApiDocsEnabled(this IConfiguration configuration, IHostEnvironment environment)
        => configuration.GetValue("ApiDocs:Enabled", environment.IsDevelopment());
}
