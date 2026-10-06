namespace YGOProbabilityCalculatorBlazor.Models;

public enum CategorySource
{
    User,
    Metadata
}

public record CategoryBase
{
    public string Name { get; }
    public CategorySource Source { get; }
    public string? MetadataKey { get; }
    public string Identity => Source == CategorySource.User ? $"user:{Name}" : $"metadata:{MetadataKey}";

    public CategoryBase(string name, CategorySource source = CategorySource.User, string? metadataKey = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category cannot be empty.", nameof(name));
        }

        if (! Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (source == CategorySource.Metadata && string.IsNullOrWhiteSpace(metadataKey))
        {
            throw new ArgumentException("Card properties require a stable metadata key.", nameof(metadataKey));
        }

        if (source == CategorySource.User && metadataKey is not null)
        {
            throw new ArgumentException("User categories cannot have a metadata key.", nameof(metadataKey));
        }

        Name = name;
        Source = source;
        MetadataKey = metadataKey;
    }

    public virtual bool Equals(CategoryBase? other) => other is not null &&
                                                       string.Equals(Identity,
                                                           other.Identity,
                                                           StringComparison.Ordinal
                                                       );

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
}
