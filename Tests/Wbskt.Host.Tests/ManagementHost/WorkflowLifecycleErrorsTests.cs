using Wbskt.Host.Tests.AuthHost;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Workflow;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// The one place the lifecycle procedures' refusals become API errors. Each number maps to the status
/// the conventions give it; a number that needs its caller's context, or a fault, is not mapped here.
/// </summary>
public sealed class WorkflowLifecycleErrorsTests
{
    private static readonly Guid RefId = Guid.Parse("12121212-1212-1212-1212-121212121212");

    [Theory]
    [InlineData(50040, "WORKFLOW_NOT_FOUND", ErrorType.NotFound)]
    [InlineData(50041, "WORKFLOW_DEPRECATED", ErrorType.Conflict)]
    [InlineData(50042, "WORKFLOW_NOT_DEPRECATED", ErrorType.Conflict)]
    [InlineData(50043, "WORKFLOW_VERSION_NOT_FOUND", ErrorType.NotFound)]
    [InlineData(50023, "WORKFLOW_VALIDATION_FAILED", ErrorType.Validation)]
    [InlineData(50022, "WORKFLOW_DELETED", ErrorType.Conflict)]
    public void A_refusal_maps_to_its_error(int number, string code, ErrorType type)
    {
        Assert.True(WorkflowLifecycleErrors.TryMap(SqlExceptions.WithNumber(number, "refused"), RefId, 3, out Error error));

        Assert.Equal(code, error.Code);
        Assert.Equal(type, error.Type);
    }

    [Fact]
    public void A_missing_version_names_the_version_asked_for()
    {
        WorkflowLifecycleErrors.TryMap(SqlExceptions.WithNumber(50043, "refused"), RefId, 7, out Error error);

        Assert.Contains("version 7", error.Message);
        Assert.Contains(RefId.ToString(), error.Message);
    }

    [Theory]
    [InlineData(50021)]
    [InlineData(50024)]
    [InlineData(50025)]
    [InlineData(1205)]
    [InlineData(2627)]
    public void A_number_that_needs_its_callers_context_or_a_fault_is_not_mapped(int number)
    {
        Assert.False(WorkflowLifecycleErrors.TryMap(SqlExceptions.WithNumber(number, "fault"), RefId, null, out _));
    }
}
