using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Binds the internal ID of the workspace the permission filter resolved for this request. Only
/// valid on an action that carries <see cref="RequiresPermissionAttribute"/>, and not part of the
/// public API surface: the caller still addresses the workspace by its reference.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromWorkspaceAttribute : ModelBinderAttribute
{
    public FromWorkspaceAttribute() : base(typeof(ScopeIdModelBinder<RequiresPermissionAttribute>))
    {
        BindingSource = BindingSource.Special;
    }
}

/// <summary>Binds the scope id that <see cref="ScopedPermissionFilter{TAttribute}"/> stored for this request.</summary>
public sealed class ScopeIdModelBinder<TAttribute> : IModelBinder where TAttribute : PermissionRequirementAttribute
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (!ScopedPermissionFilter<TAttribute>.TryGetScopeId(bindingContext.HttpContext, out var scopeId))
        {
            // A wiring mistake, not a caller's: the action asked for a scope it never required.
            throw new InvalidOperationException(
                $"{bindingContext.ActionContext.ActionDescriptor.DisplayName} binds its {typeof(TAttribute).Name} scope id but carries no [{typeof(TAttribute).Name}].");
        }

        bindingContext.Result = ModelBindingResult.Success(scopeId);
        return Task.CompletedTask;
    }
}
