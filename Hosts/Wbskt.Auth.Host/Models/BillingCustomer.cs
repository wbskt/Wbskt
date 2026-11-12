namespace Wbskt.Auth.Host.Models;

/// <summary>
/// The customer a tenant pays a provider as. <c>Shared</c> is true when another tenant has paid as the
/// same customer, which a checkout made before each tenant had its own could do: the provider matched
/// the payer's email to an existing customer.
/// </summary>
public sealed record BillingCustomer(string CustomerId, bool Shared);
