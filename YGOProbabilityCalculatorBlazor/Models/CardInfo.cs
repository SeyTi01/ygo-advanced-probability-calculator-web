namespace YGOProbabilityCalculatorBlazor.Models;

/// <summary>Reusable objective fields from YGOPRODeck's cardinfo endpoint.</summary>
public sealed record CardInfo {
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? FrameType { get; init; }
    public string? Race { get; init; }
    public string? Attribute { get; init; }
    public int? Level { get; init; }
    public int? LinkVal { get; init; }
    public int? Scale { get; init; }
    public string? Archetype { get; init; }
    public int? CanonicalCardId { get; init; }
    public IReadOnlyList<int> ArtworkImageIds { get; init; } = Array.Empty<int>();
    public bool ArtworkMetadataKnown { get; init; }

    public int? SelectArtworkImageId(int importedPasscode) {
        if (ArtworkImageIds.Contains(importedPasscode)) {
            return importedPasscode;
        }

        if (CanonicalCardId is { } canonical && ArtworkImageIds.Contains(canonical)) {
            return canonical;
        }

        return ArtworkImageIds.Count > 0 ? ArtworkImageIds[0] : null;
    }
}
