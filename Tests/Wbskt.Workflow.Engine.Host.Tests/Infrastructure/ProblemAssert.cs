using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Wbskt.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>Reads the Problem Details an action or filter answered with.</summary>
public static class ProblemAssert
{
    /// <summary>The result is a problem with <paramref name="status"/>; returns its code.</summary>
    public static string Is(object? result, int status)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result is IConvertToActionResult convert ? convert.Convert() : result);
        Assert.Equal(status, objectResult.StatusCode);
        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Equal(status, problem.Status);
        return Assert.IsType<string>(Problems.CodeOf(problem));
    }

    /// <summary>The problem's <c>detail</c>, which carries the error's message.</summary>
    public static string Detail(object? result) =>
        Assert.IsAssignableFrom<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value).Detail!;
}
