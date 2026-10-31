namespace Wbskt.Workflow.Abstraction.Templates;

/// <summary>A step whose config has <see cref="TemplatedAttribute">templated</see> fields.</summary>
public interface ITemplatedStep
{
    object? TemplatedConfig { get; }
}
