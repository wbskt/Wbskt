using System.Text.Json;

namespace Wbskt.Management.Models;

/// <summary>
/// Adds an integration. <c>Type</c> is <c>telegram</c>, <c>discord</c>, <c>smtp</c>, <c>ntfy</c> or
/// <c>httpAuth</c>; <c>Preset</c> is optional (<c>homeAssistant</c>, an httpAuth shape). <c>Config</c>
/// holds the type's settings and <c>Secrets</c> its credential, which is sealed and never returned.
/// </summary>
public sealed record IntegrationCreateRequest(
    string Type,
    string? Preset,
    string Name,
    JsonElement? Config,
    JsonElement? Secrets);

/// <summary>
/// Changes an integration. Every field is optional. <c>Config</c> replaces the settings whole;
/// <c>Secrets</c> is merged over the saved credential: a field it sets replaces that field, a field it
/// leaves out keeps its value, and an empty string clears an optional one. <c>Type</c>, when given,
/// must be the integration's own: a type cannot change.
/// </summary>
public sealed record IntegrationUpdateRequest(
    string? Name,
    string? Type,
    JsonElement? Config,
    JsonElement? Secrets);

/// <summary>What a test may do beyond checking the credential: send to a Telegram chat, or mail an address.</summary>
public sealed record IntegrationTestOptionsDto(string? ChatId, string? SendTo);

/// <summary>
/// Tries an integration without saving anything. With <c>RefId</c> alone it tests the saved
/// integration (and records the result on it); with <c>Config</c> or <c>Secrets</c> too, it tests the
/// saved integration with those edits applied, as the edit form would save it. Without <c>RefId</c>
/// it tests a new integration from <c>Type</c>, <c>Config</c> and <c>Secrets</c>.
/// </summary>
public sealed record IntegrationTestRequest(
    Guid? RefId,
    string? Type,
    string? Preset,
    JsonElement? Config,
    JsonElement? Secrets,
    IntegrationTestOptionsDto? Options);

/// <summary>The outcome of a test. A failed test is a 200 with <c>Ok</c> false, a code and a message safe to show.</summary>
public sealed record IntegrationTestResponse(bool Ok, string? Code, string? Message, JsonElement? Metadata);

public sealed record IntegrationLastTest(DateTime At, bool Ok, string? Code, string? Message);

/// <summary>How many live workflows, and how many of their steps, send through the integration.</summary>
public sealed record IntegrationUsageSummary(int Workflows, int Steps);

/// <summary>Who added the integration, by their user reference; the console resolves the name.</summary>
public sealed record IntegrationCreator(Guid? UserRef);

/// <summary>
/// An integration as the console shows it. Never carries the secret: <c>SecretHint</c> identifies it
/// (its last characters) without revealing it.
/// </summary>
public sealed record IntegrationResponse(
    Guid RefId,
    string Type,
    string Category,
    string? Preset,
    string Name,
    JsonElement Config,
    string? SecretHint,
    JsonElement? Metadata,
    IntegrationLastTest? LastTest,
    DateTime? LastUsedAt,
    IntegrationUsageSummary Usage,
    IntegrationCreator CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SecretRotatedAt);

/// <summary>
/// One step that sends through an integration, in a live workflow's current version.
/// <c>Status</c> is <c>published</c> (blocks deleting the integration) or <c>deprecated</c> (does not).
/// </summary>
public sealed record IntegrationUsedBy(Guid WorkflowRefId, string WorkflowName, Guid StepId, string StepName, string Status);

public sealed record IntegrationUsageResponse(IReadOnlyList<IntegrationUsedBy> UsedBy);
