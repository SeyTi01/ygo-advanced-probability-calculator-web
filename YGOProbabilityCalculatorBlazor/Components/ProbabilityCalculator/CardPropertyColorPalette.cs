using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class CardPropertyColorPalette
{
    public const string GenericMetadataClass = "card-property-color-generic";
    public const string GenericAttributeClass = "card-property-color-attribute";
    public const string MonsterTraitClass = "card-property-color-monster-trait";

    private static readonly IReadOnlyDictionary<string, string> AttributeClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fire"] = "card-property-color-attribute-fire",
            ["water"] = "card-property-color-attribute-water",
            ["wind"] = "card-property-color-attribute-wind",
            ["earth"] = "card-property-color-attribute-earth",
            ["light"] = "card-property-color-attribute-light",
            ["dark"] = "card-property-color-attribute-dark",
            ["divine"] = "card-property-color-attribute-divine"
        };

    private static readonly IReadOnlyDictionary<string, string> MonsterTypeClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ritual"] = "card-property-color-ritual",
            ["fusion"] = "card-property-color-fusion",
            ["synchro"] = "card-property-color-synchro",
            ["xyz"] = "card-property-color-xyz",
            ["link"] = "card-property-color-monster-link"
        };

    public static string GetCssClass(CategoryBase category) =>
        category.Source == CategorySource.Metadata
            ? GetCssClass(category.MetadataKey)
            : GenericMetadataClass;

    public static string GetCssClass(string? metadataKey)
    {
        if (string.IsNullOrWhiteSpace(metadataKey))
        {
            return GenericMetadataClass;
        }

        string key = metadataKey.Trim().ToLowerInvariant();
        int separator = key.IndexOf(':');

        if (separator <= 0 || separator == key.Length - 1)
        {
            return GenericMetadataClass;
        }

        string family = key[..separator];
        string value = key[(separator + 1)..];

        return family switch
        {
            "attribute" => AttributeClasses.TryGetValue(value, out string? attributeClass)
                ? attributeClass
                : GenericAttributeClass,
            "kind" => value switch
            {
                "monster" => "card-property-color-monster",
                "spell" => "card-property-color-spell",
                "trap" => "card-property-color-trap",
                _ => GenericMetadataClass
            },
            "spell-type" => "card-property-color-spell",
            "trap-type" => "card-property-color-trap",
            "monster-trait" => GetMonsterTraitClass(value),
            "monster-type" => MonsterTypeClasses.TryGetValue(value, out string? monsterTypeClass)
                ? monsterTypeClass
                : GenericMetadataClass,
            "level" => "card-property-color-level",
            "rank" => "card-property-color-rank",
            "link" => "card-property-color-link-rating",
            "scale" => "card-property-color-scale",
            "monster-race" => "card-property-color-monster-race",
            "archetype" => "card-property-color-archetype",
            _ => GenericMetadataClass
        };
    }

    private static string GetMonsterTraitClass(string value) => value switch
    {
        "normal" => "card-property-color-normal",
        "effect" => "card-property-color-effect",
        "pendulum" => "card-property-color-pendulum",
        _ => MonsterTraitClass
    };
}
