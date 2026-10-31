namespace Wbskt.Workflow.Abstraction.Templates;

/// <summary>
/// Marks a step config property whose <c>{{ }}</c> placeholders are filled in from the branch when the
/// step runs (see <see cref="TemplateFields"/>). On a string, a list of strings or a dictionary's values
/// it fills the text; on a <see cref="System.Text.Json.JsonElement"/> every string inside it; on a
/// record, or a list of records, it reaches into their own marked properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TemplatedAttribute(TemplateText text = TemplateText.Free) : Attribute
{
    public TemplateText Text { get; } = text;
}

public enum TemplateText
{
    /// <summary>A message, a body, a title.</summary>
    Free,

    /// <summary>
    /// A header, a URL or an id: line breaks a placeholder brings in become spaces, so a value cannot
    /// start a header of its own.
    /// </summary>
    Line
}
