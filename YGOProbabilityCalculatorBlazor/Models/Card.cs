using System.Collections.Frozen;

namespace YGOProbabilityCalculatorBlazor.Models;

public class Card
{
    public string Id { get; }
    public int Copies { get; }
    public List<CategoryBase> Categories { get; }
    public string? Name { get; }
    public bool Active { get; }
    public int? ExternalCardId { get; }
    public IReadOnlySet<string> ManualMetadataCategoryKeys { get; }

    public Card(
        IEnumerable<CategoryBase> categories,
        int copies = 1,
        string? name = null,
        bool active = true,
        string? id = null,
        int? externalCardId = null,
        IEnumerable<string>? manualMetadataCategoryKeys = null
    )
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Copies = copies;
        Categories = [.. categories];
        Name = name;
        Active = active;
        ExternalCardId = externalCardId;
        ManualMetadataCategoryKeys = ValidateManualKeys(Categories, manualMetadataCategoryKeys);
    }

    public Card WithName(string? name) =>
        new(Categories, Copies, name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithCopies(int copies) =>
        new(Categories, copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithCategories(IEnumerable<CategoryBase> categories)
    {
        List<CategoryBase> effective = [.. categories];

        return new(effective,
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(key =>
                effective.Any(c => c.Source == CategorySource.Metadata && c.MetadataKey == key)
            )
        );
    }

    public Card WithActive(bool active) =>
        new(Categories, Copies, Name, active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithManualMetadataCategory(CategoryBase category)
    {
        if (category.Source != CategorySource.Metadata)
        {
            throw new ArgumentException("Manual card properties require a metadata category.", nameof(category));
        }

        // An existing objective membership must never become a removable override.
        if (Categories.Contains(category))
        {
            return this;
        }

        return new(Categories.Concat([category]),
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Append(category.MetadataKey!)
        );
    }

    public Card WithoutManualMetadataCategory(string key)
    {
        if (! ManualMetadataCategoryKeys.Contains(key))
        {
            return this;
        }

        return new(Categories.Where(c => c.Source != CategorySource.Metadata || c.MetadataKey != key),
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(k => k != key)
        );
    }

    public Card WithObjectiveMetadata(IEnumerable<CategoryBase> properties, int externalCardId)
    {
        List<CategoryBase> objective = [.. properties];

        if (objective.Any(c => c.Source != CategorySource.Metadata))
        {
            throw new ArgumentException("Objective card properties require metadata categories.", nameof(properties));
        }

        HashSet<string> objectiveKeys = objective.Select(c => c.MetadataKey!).ToHashSet(StringComparer.Ordinal);
        IEnumerable<CategoryBase> effective = Categories
            .Where(c => c.Source == CategorySource.User || ManualMetadataCategoryKeys.Contains(c.MetadataKey!))
            .Concat(objective)
            .DistinctBy(c => c.Identity);

        // Once materialized objectively, an overlapping override is no longer removable.
        return new(effective,
            Copies,
            Name,
            Active,
            Id,
            externalCardId,
            ManualMetadataCategoryKeys.Where(key => ! objectiveKeys.Contains(key))
        );
    }

    private static IReadOnlySet<string> ValidateManualKeys(
        IEnumerable<CategoryBase> categories,
        IEnumerable<string>? keys
    )
    {
        FrozenSet<string> result = (keys ?? []).ToFrozenSet(StringComparer.Ordinal);
        HashSet<string?> metadataKeys = categories
            .Where(c => c.Source == CategorySource.Metadata)
            .Select(c => c.MetadataKey)
            .ToHashSet(StringComparer.Ordinal);

        if (result.Any(key => string.IsNullOrWhiteSpace(key) || ! metadataKeys.Contains(key)))
        {
            throw new ArgumentException("Manual card properties must refer to effective metadata categories.",
                nameof(keys)
            );
        }

        return result;
    }
}
