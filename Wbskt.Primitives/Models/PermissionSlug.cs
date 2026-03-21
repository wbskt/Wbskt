namespace Wbskt.Primitives.Models;

public readonly struct PermissionSlug : IEquatable<PermissionSlug>
{
    private string Value { get; }

    // Private constructor prevents external instantiation
    private PermissionSlug(string value) => Value = value;

    // We only allow the Constants class to "create" these
    internal static PermissionSlug CreateInternal(string value) => new(value);

    public static implicit operator string(PermissionSlug slug) => slug.Value;
    public override string ToString() => Value;
    
    // Equality members for struct performance
    public bool Equals(PermissionSlug other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is PermissionSlug other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    public static bool operator ==(PermissionSlug left, PermissionSlug right) => left.Equals(right);
    public static bool operator !=(PermissionSlug left, PermissionSlug right) => !left.Equals(right);
}