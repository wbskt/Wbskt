using System.Text.Json;

namespace Wbskt.Workflow.Runtime;

public abstract class NodeExecutionResult
{
    public static SuccessResult Success(JsonElement? output, string? takePort, bool skipSave)
    {
        return new SuccessResult
        {
            Output = output,
            TakePort = takePort,
            SkipSave = skipSave
        };
    }

    public static ForkResult Fork(IReadOnlyList<BranchContext> children, string? cohortId)
    {
        return new ForkResult
        {
            Children = children,
            CohortId = cohortId
        };
    }

    public static BookmarkResult Bookmark(string matchKey, TimeSpan? ttl, string? ttlPort, JsonElement? state)
    {
        return new BookmarkResult
        {
            MatchKey = matchKey,
            Ttl = ttl,
            TtlPort = ttlPort,
            State = state
        };
    }

    public static FailResult Fail(string errorMessage, string? errorCode, JsonElement? errorDetails)
    {
        return new FailResult
        {
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            ErrorDetails = errorDetails
        };
    }

    public static TerminalResult Terminal()
    {
        return new TerminalResult();
    }

    public static CompensationResult Compensation(IReadOnlyList<Guid> branchesToCompensate)
    {
        return new CompensationResult
        {
            BranchesToCompensate = branchesToCompensate
        };
    }

    public sealed class SuccessResult : NodeExecutionResult
    {
        public required JsonElement? Output { get; init; }

        public required string? TakePort { get; init; }

        public required bool SkipSave { get; init; }
    }

    public sealed class ForkResult : NodeExecutionResult
    {
        public required IReadOnlyList<BranchContext> Children { get; init; }

        public required string? CohortId { get; init; }
    }

    public sealed class BookmarkResult : NodeExecutionResult
    {
        public required string MatchKey { get; init; }

        public required TimeSpan? Ttl { get; init; }

        public required string? TtlPort { get; init; }

        public required JsonElement? State { get; init; }
    }

    public sealed class FailResult : NodeExecutionResult
    {
        public required string ErrorMessage { get; init; }

        public required string? ErrorCode { get; init; }

        public required JsonElement? ErrorDetails { get; init; }
    }

    public sealed class TerminalResult : NodeExecutionResult
    {
    }

    public sealed class CompensationResult : NodeExecutionResult
    {
        public required IReadOnlyList<Guid> BranchesToCompensate { get; init; }
    }
}
