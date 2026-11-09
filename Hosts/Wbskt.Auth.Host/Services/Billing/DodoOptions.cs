namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Dodo Payments (<c>Billing:Dodo</c>). The API key and the webhook signing secret are secrets: they come
/// from the environment (<c>Billing__Dodo__ApiKey</c>, or Dodo's own <c>DODO_PAYMENTS_API_KEY</c> and
/// <c>DODO_PAYMENTS_WEBHOOK_KEY</c>), never from a file in the repository. Without both, payments are off.
/// </summary>
public sealed class DodoOptions
{
    public const string SectionName = "Billing:Dodo";

    public string? ApiKey { get; set; }

    public string? WebhookSecret { get; set; }

    /// <summary><c>test_mode</c> or <c>live_mode</c>; picks the API's base address.</summary>
    public string Environment { get; set; } = TestMode;

    /// <summary>Overrides the base address <see cref="Environment"/> picks, for a local stand-in.</summary>
    public string? BaseUrl { get; set; }

    public const string TestMode = "test_mode";

    public const string LiveMode = "live_mode";

    /// <summary>
    /// Refuses to start on an <see cref="Environment"/> that is neither mode: a typo would otherwise send
    /// a live key to the test API, and payments would fail only when a customer first pays.
    /// </summary>
    public void EnsureValid()
    {
        if (Environment is not (TestMode or LiveMode))
        {
            throw new InvalidOperationException($"{SectionName}:Environment must be '{TestMode}' or '{LiveMode}', not '{Environment}'.");
        }
    }

    public Uri ResolveBaseAddress() => new(
        !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl.TrimEnd('/') + "/"
        : Environment == LiveMode ? "https://live.dodopayments.com/"
        : "https://test.dodopayments.com/");

    /// <summary>Binds the section and falls back to the variable names Dodo's own tooling uses.</summary>
    public static DodoOptions From(IConfiguration configuration)
    {
        DodoOptions options = configuration.GetSection(SectionName).Get<DodoOptions>() ?? new DodoOptions();
        options.ApiKey = Blank(options.ApiKey) ?? Blank(configuration["DODO_PAYMENTS_API_KEY"]);
        options.WebhookSecret = Blank(options.WebhookSecret) ?? Blank(configuration["DODO_PAYMENTS_WEBHOOK_KEY"]) ?? Blank(configuration["DODO_WEBHOOK_SECRET"]);
        return options;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
