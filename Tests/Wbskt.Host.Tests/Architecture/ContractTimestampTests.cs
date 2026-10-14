using System.Reflection;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// A timestamp on the wire is a <see cref="DateTimeOffset"/>. A <see cref="DateTime"/> serializes
/// without an offset whenever its <see cref="DateTime.Kind"/> is not Utc, and a reader on the other
/// side then guesses the zone. The rule reads every public type in the contract packages (the
/// <c>*.Models</c> assemblies and <c>Wbskt.Protocol</c>, as in <see cref="ContractPackageTests"/>).
/// </summary>
public sealed class ContractTimestampTests
{
    private static readonly Assembly[] ContractPackages =
    [
        typeof(global::Wbskt.Auth.Models.LoginRequest).Assembly,
        typeof(global::Wbskt.Management.Models.EventLogResponse).Assembly,
        typeof(global::Wbskt.Models.Page<>).Assembly,
        typeof(global::Wbskt.Protocol.SocketMessage).Assembly,
    ];

    /// <summary>
    /// The <see cref="DateTime"/> fields the contracts already had when the rule arrived, as
    /// <c>Type.Property</c>. Shrink this list, never grow it: a new field is a
    /// <see cref="DateTimeOffset"/>, and moving one of these is a wire change for its readers (the
    /// console, the SDK, the typed client), so it goes out with them. An entry that is no longer a
    /// <see cref="DateTime"/> fails the test too, so the fix also removes it here.
    /// </summary>
    private static readonly HashSet<string> ExistingDateTimeFields =
    [
        // Wbskt.Auth.Models
        "CreatedInvitationResponse.ExpiresAt",
        "InvitationResponse.CreatedAt",
        "InvitationResponse.ExpiresAt",
        "SessionResponse.CreatedAt",
        "SessionResponse.ExpiresAt",
        "SessionResponse.LastUsedAt",
        "WorkspaceResponse.CreatedAt",

        // Wbskt.Management.Models
        "BranchSummaryDto.CreatedAt",
        "BranchSummaryDto.UpdatedAt",
        "ClientDetailResponse.ConnectedAt",
        "ClientDetailResponse.CreatedAt",
        "ClientDetailResponse.LastActivityAt",
        "ClientDetailResponse.RttMeasuredAt",
        "ClientReadingBucket.Start",
        "ClientReadingsResponse.From",
        "ClientReadingsResponse.To",
        "ClientResponse.ConnectedAt",
        "ClientResponse.CreatedAt",
        "ClientResponse.LastActivityAt",
        "ClientStateVariableResponse.UpdatedAt",
        "EventLogResponse.CreatedAtUtc",
        "EventLogSummaryResponse.FromUtc",
        "EventLogSummaryResponse.ToUtc",
        "FailureBucketDto.LastSeenAt",
        "HistoryEventDto.Timestamp",
        "IntegrationLastTest.At",
        "IntegrationResponse.CreatedAt",
        "IntegrationResponse.LastUsedAt",
        "IntegrationResponse.SecretRotatedAt",
        "IntegrationResponse.UpdatedAt",
        "MessageTemplateResponse.CreatedAt",
        "MessageTemplateResponse.UpdatedAt",
        "RegistrationPolicyResponse.CreatedAt",
        "RunSummaryDto.CompletedAt",
        "RunSummaryDto.StartedAt",
        "SharedVariableDto.UpdatedAt",
        "WorkflowDefinitionDto.CreatedAt",
        "WorkflowStatsResponse.FromUtc",
        "WorkflowStatsResponse.ToUtc",
        "WorkflowSummaryDto.CreatedAt",
        "WorkflowVersionDto.CreatedAt",
        "WorkspaceStatsResponse.FromUtc",
        "WorkspaceStatsResponse.ToUtc",

        // Wbskt.Protocol
        "PingPayload.Timestamp",
        "PongPayload.OriginalTimestamp",
    ];

    [Fact]
    public void No_new_contract_field_is_a_DateTime()
    {
        var found = ContractPackages
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(p => CarriesDateTime(p.PropertyType))
                .Select(p => $"{t.Name}.{p.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var added = found.Except(ExistingDateTimeFields).ToList();
        var fixedOnes = ExistingDateTimeFields.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            added.Count == 0,
            "A contract timestamp is a DateTimeOffset, not a DateTime. Change these fields:"
            + Environment.NewLine + string.Join(Environment.NewLine, added));
        Assert.True(
            fixedOnes.Count == 0,
            "These are no longer DateTime fields; remove them from ExistingDateTimeFields:"
            + Environment.NewLine + string.Join(Environment.NewLine, fixedOnes));
    }

    private static bool CarriesDateTime(Type type)
    {
        if (type == typeof(DateTime))
        {
            return true;
        }

        if (type.HasElementType)
        {
            return CarriesDateTime(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(CarriesDateTime);
    }
}
