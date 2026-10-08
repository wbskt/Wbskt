using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Wbskt.Management.Host.Authorization;

/// <summary>
/// Binds the internal ID of the workspace <see cref="WorkspacePermissionFilter"/> resolved for this
/// request. Only valid on an action that carries <see cref="RequiresPermissionAttribute"/>, and not
/// part of the public API surface: the caller still addresses the workspace by its reference.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromWorkspaceAttribute : ModelBinderAttribute
{
    public FromWorkspaceAttribute() : base(typeof(WorkspaceIdModelBinder))
    {
        BindingSource = BindingSource.Special;
    }
}

internal sealed class WorkspaceIdModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (!WorkspacePermissionFilter.TryGetWorkspaceId(bindingContext.HttpContext, out var workspaceId))
        {
            // A wiring mistake, not a caller's: the action asked for a workspace it never required.
            throw new InvalidOperationException(
                $"{bindingContext.ActionContext.ActionDescriptor.DisplayName} binds [FromWorkspace] but carries no [RequiresPermission].");
        }

        bindingContext.Result = ModelBindingResult.Success(workspaceId);
        return Task.CompletedTask;
    }
}
