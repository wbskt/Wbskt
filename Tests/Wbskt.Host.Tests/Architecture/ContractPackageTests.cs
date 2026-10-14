using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wbskt.Infrastructure;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// "Models live in model files" (Docs/Coding.Conventions.md): what a public action of the auth or
/// management host returns or reads from its body is declared in a contract package, so the other
/// host, the E2E tests, the typed client (<c>Clients/Wbskt.Api.Client</c>) and the console share one
/// type instead of keeping copies of it.
/// </summary>
/// <remarks>
/// A contract package is an assembly whose name ends in <c>.Models</c>, or <c>Wbskt.Protocol</c>, the
/// device protocol the device-facing routes speak. BCL types (<c>JsonElement</c>, <c>string</c>, ...)
/// are allowed as they are. Collections, arrays, nullables and generic contract wrappers such as
/// <c>Page&lt;T&gt;</c> are unwrapped, and every type they carry is checked. Routes under
/// <see cref="InternalRoutes.Prefix"/> are checked too: their contracts live in the packages as well.
/// </remarks>
public sealed class ContractPackageTests
{
    private static readonly Assembly AuthHost = typeof(global::Wbskt.Auth.Host.Program).Assembly;
    private static readonly Assembly ManagementHost = typeof(global::Wbskt.Management.Host.Program).Assembly;

    private const string DeviceProtocol = "Wbskt.Protocol";

    [Fact]
    public void Every_public_action_returns_and_binds_types_from_a_contract_package()
    {
        var checkedTypes = new HashSet<Type>();
        var offending = new List<string>();
        foreach (var action in new[] { AuthHost, ManagementHost }.SelectMany(HostArchitectureTests.ActionsIn))
        {
            if (DeclaredResponse(action.Method) is { } response)
            {
                foreach (Type type in ContractTypes(response))
                {
                    checkedTypes.Add(type);
                    if (!IsContract(type))
                    {
                        offending.Add($"{action.Name} returns {Describe(type)}");
                    }
                }
            }

            foreach (var parameter in action.Method.GetParameters().Where(IsBody))
            {
                foreach (Type type in ContractTypes(parameter.ParameterType))
                {
                    checkedTypes.Add(type);
                    if (!IsContract(type))
                    {
                        offending.Add($"{action.Name} binds {parameter.Name} as {Describe(type)}");
                    }
                }
            }
        }

        // The rule reads what it finds, so pin that it finds something on both sides of the wire.
        Assert.Contains(checkedTypes, t => t.Name == "EventLogResponse");
        Assert.Contains(checkedTypes, t => t.Name == "LoginRequest");

        Assert.True(
            offending.Count == 0,
            "Public request and response types must live in a contract package (an assembly named *.Models, or "
            + $"{DeviceProtocol}), not in a host. Move each of these into Wbskt.Auth.Models or Wbskt.Management.Models:"
            + Environment.NewLine + string.Join(Environment.NewLine, offending.Distinct().Order(StringComparer.Ordinal)));
    }

    private static bool IsContract(Type type)
    {
        string assembly = type.Assembly.GetName().Name ?? string.Empty;
        return assembly.EndsWith(".Models", StringComparison.Ordinal)
               || assembly == DeviceProtocol
               || IsBcl(type);
    }

    private static bool IsBcl(Type type) =>
        type.Assembly == typeof(object).Assembly
        || type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));

    private static string Describe(Type type) => $"{type.FullName} (declared in {type.Assembly.GetName().Name})";

    /// <summary>
    /// The types a declared shape is made of: itself, or for an array, a nullable or a generic type, the
    /// types it carries as well. A generic BCL collection is only a wrapper; a generic contract type such
    /// as <c>Page&lt;T&gt;</c> is checked itself and for its arguments.
    /// </summary>
    private static IEnumerable<Type> ContractTypes(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return ContractTypes(underlying);
        }

        if (type.IsArray)
        {
            return ContractTypes(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            var carried = type.GetGenericArguments().SelectMany(ContractTypes);
            return IsBcl(definition) ? carried : carried.Prepend(definition);
        }

        return [type];
    }

    /// <summary>The T of <c>Task&lt;ActionResult&lt;T&gt;&gt;</c>, <c>ActionResult&lt;T&gt;</c> or <c>Task&lt;T&gt;</c>.</summary>
    private static Type? DeclaredResponse(MethodInfo method)
    {
        Type type = method.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            type = type.GetGenericArguments()[0];
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
        {
            return type.GetGenericArguments()[0];
        }

        return typeof(IActionResult).IsAssignableFrom(type) || type == typeof(Task) || type == typeof(void) ? null : type;
    }

    /// <summary>
    /// Whether a parameter is read from the body: <c>[FromBody]</c>, or, under <c>[ApiController]</c>, a
    /// complex type with no binding attribute of its own.
    /// </summary>
    private static bool IsBody(ParameterInfo parameter)
    {
        var sources = parameter.GetCustomAttributes(inherit: true).OfType<IBindingSourceMetadata>().ToList();
        if (sources.Count > 0)
        {
            return sources.Any(s => s.BindingSource == BindingSource.Body);
        }

        Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return !(type.IsPrimitive
                 || type.IsEnum
                 || type == typeof(string)
                 || type == typeof(decimal)
                 || type == typeof(Guid)
                 || type == typeof(DateTime)
                 || type == typeof(DateTimeOffset)
                 || type == typeof(TimeSpan)
                 || type == typeof(CancellationToken));
    }
}
