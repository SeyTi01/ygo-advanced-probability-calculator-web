using System.Collections.Frozen;

namespace YGOProbabilityCalculatorBlazor.Models;

public class Card {
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
    ) {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Copies = copies;
        Categories = categories.ToList();
        Name = name;
        Active = active;
        ExternalCardId = externalCardId;
        ManualMetadataCategoryKeys = ValidateManualKeys(Categories, manualMetadataCategoryKeys);
    }

    public Card WithName(string? name) => new(Categories, Copies, name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithCopies(int copies) => new(Categories, copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithCategories(IEnumerable<CategoryBase> categories) {
        List<CategoryBase> effective = categories.ToList();

        return new(
            effective,
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(key => effective.Any(category => category.Source == CategorySource.Metadata && category.MetadataKey == key))
        );
    }

    public Card WithActive(bool active) => new(Categories, Copies, Name, active, Id, ExternalCardId, ManualMetadataCategoryKeys);

    public Card WithManualMetadataCategory(CategoryBase category) {
        if (category.Source != CategorySource.Metadata) {
            throw new ArgumentException("Manual card properties require a metadata category.", nameof(category));
        }

        // An existing objective membership must never become a removable override.
        if (Categories.Contains(category)) {
            return this;
        }

        return new(Categories.Concat([category]), Copies, Name, Active, Id, ExternalCardId, ManualMetadataCategoryKeys.Append(category.MetadataKey!));
    }

    public Card WithoutManualMetadataCategory(string key) {
        if (!ManualMetadataCategoryKeys.Contains(key)) {
            return this;
        }

        return new(
            Categories.Where(category => category.Source != CategorySource.Metadata || category.MetadataKey != key),
            Copies,
            Name,
            Active,
            Id,
            ExternalCardId,
            ManualMetadataCategoryKeys.Where(metadataKey => metadataKey != key)
        );
    }

    public Card WithObjectiveMetadata(IEnumerable<CategoryBase> properties, int externalCardId) {
        List<CategoryBase> objective = properties.ToList();

        if (objective.Any(static category => category.Source != CategorySource.Metadata)) {
            throw new ArgumentException("Objective card properties require metadata categories.", nameof(properties));
        }

        HashSet<string> objectiveKeys = objective.Select(static category => category.MetadataKey!).ToHashSet(StringComparer.Ordinal);
        IEnumerable<CategoryBase> effective = Categories
            .Where(category => category.Source == CategorySource.User || ManualMetadataCategoryKeys.Contains(category.MetadataKey!))
            .Concat(objective)
            .DistinctBy(static category => category.Identity);

        // Once materialized objectively, an overlapping override is no longer removable.
        return new(effective, Copies, Name, Active, Id, externalCardId, ManualMetadataCategoryKeys.Where(key => !objectiveKeys.Contains(key)));
    }

    private static IReadOnlySet<string> ValidateManualKeys(IEnumerable<CategoryBase> categories, IEnumerable<string>? keys) {
        FrozenSet<string> result = (keys ?? []).ToFrozenSet(StringComparer.Ordinal);
        HashSet<string?> metadataKeys = categories
            .Where(static category => category.Source == CategorySource.Metadata)
            .Select(static category => category.MetadataKey)
            .ToHashSet(StringComparer.Ordinal);

        bool hasInvalidManualKey = result.Any(key => string.IsNullOrWhiteSpace(key) || !metadataKeys.Contains(key));

        if (hasInvalidManualKey) {
            throw new ArgumentException("Manual card properties must refer to effective metadata categories.", nameof(keys));
        }

        return result;
    }
}
