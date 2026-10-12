using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// "Outcomes vs faults" in Docs/Coding.Conventions.md, on the auth host: a database guard is a result
/// the caller can act on; a fault propagates to GlobalExceptionMiddleware, which logs it once and
/// answers a 500 without the exception text.
/// </summary>
public sealed class AuthFaultTests
{
    private const int CallerId = 1;
    private const int TenantId = 5;
    private static readonly Guid TenantRef = Guid.NewGuid();

    private readonly Mock<IAuthProvider> _provider = new();
    private readonly ManagementService _service;

    public AuthFaultTests()
    {
        _provider.Setup(p => p.FindTenantIdByRefIdForUserAsync(TenantRef, CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(TenantId);
        _provider.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, null, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new ManagementService(
            _provider.Object, Mock.Of<IWorkspaceProvider>(), Mock.Of<IEventBus>(), Mock.Of<IAuthMailer>(),
            NullLogger<ManagementService>.Instance, Mock.Of<IAccessTokenRevocation>(), Mock.Of<IAuditWorkspaces>());
    }

    [Fact]
    public async Task A_database_guard_reads_back_as_a_rejected_operation()
    {
        _provider.Setup(p => p.UpdateTenantAsync(TenantId, "Acme", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50008, "Cannot remove the last administrator."));

        var result = await _service.UpdateTenantAsync(CallerId, TenantRef, new UpdateTenantRequest("Acme", null), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("AUTH_OPERATION_REJECTED", result.Error.Code);
    }

    [Fact]
    public async Task A_fault_propagates_instead_of_becoming_a_result()
    {
        _provider.Setup(p => p.UpdateTenantAsync(TenantId, "Acme", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(-2, "Execution Timeout Expired."));

        await Assert.ThrowsAsync<SqlException>(() => _service.UpdateTenantAsync(CallerId, TenantRef, new UpdateTenantRequest("Acme", null), CancellationToken.None));
    }

    [Fact]
    public async Task A_fault_while_authorizing_propagates()
    {
        _provider.Setup(p => p.VerifyPermissionAsync(CallerId, TenantId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection pool exhausted"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateTenantAsync(CallerId, TenantRef, new UpdateTenantRequest("Acme", null), CancellationToken.None));
    }

    [Theory]
    [InlineData(2627, true)]
    [InlineData(2627, false)]
    public void A_duplicate_is_a_conflict_only_where_the_operation_names_one(int number, bool withConflict)
    {
        var conflict = Error.Conflict("ROLE_NAME_TAKEN", "taken");

        bool translated = SqlGuardErrors.TryTranslate(SqlExceptions.WithNumber(number, "duplicate"), withConflict ? conflict : null, out var error);

        Assert.Equal(withConflict, translated);
        if (withConflict)
        {
            Assert.Same(conflict, error);
        }
    }

    [Theory]
    [InlineData(50011, "INVITATION_INVALID")]
    [InlineData(50012, "AUTH_ALREADY_MEMBER")]
    [InlineData(50017, "WORKSPACE_NOT_FOUND")]
    [InlineData(50019, "AUTH_OPERATION_REJECTED")]
    public void Each_guard_has_its_error(int number, string code)
    {
        Assert.True(SqlGuardErrors.TryTranslate(SqlExceptions.WithNumber(number, "guard"), null, out var error));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData(50020)]
    [InlineData(1205)]
    public void Anything_else_from_the_database_is_a_fault(int number)
    {
        Assert.False(SqlGuardErrors.TryTranslate(SqlExceptions.WithNumber(number, "fault"), null, out _));
    }
}

/// <summary>SqlException has no public constructor; this builds one with a given error number.</summary>
internal static class SqlExceptions
{
    public static SqlException WithNumber(int number, string message)
    {
        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        object?[] errorArgs = errorCtor.GetParameters().Select(p => p.Name switch
        {
            "infoNumber" => number,
            "errorMessage" => message,
            _ => p.ParameterType == typeof(string) ? string.Empty
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                : null
        }).ToArray();
        var error = (SqlError)errorCtor.Invoke(errorArgs);

        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(collection, [error]);

        var create = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .First(m => m.Name == "CreateException" && m.GetParameters() is [{ ParameterType: var a }, { ParameterType: var b }] && a == typeof(SqlErrorCollection) && b == typeof(string));
        return (SqlException)create.Invoke(null, [collection, "16.0"])!;
    }
}
