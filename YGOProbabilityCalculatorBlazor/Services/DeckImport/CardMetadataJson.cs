using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

// Adapts provider records and persisted metadata to the same conservative model.
// Neither source can establish the bundled catalog's verified memberships.
internal static class CardMetadataJson {
    internal static CardInfo? ReadProvider(JsonElement item, int? queryId = null) => Read(item, queryId);

    internal static CardInfo? ReadCached(JsonElement item, int queryId) => Read(item, queryId, fromCache: true);

    private static CardInfo? Read(JsonElement item, int? cacheId = null, bool fromCache = false) {
        if (item.ValueKind != JsonValueKind.Object) {
            return null;
        }

        int? id = cacheId ?? Number("id");
        string? name = Text("name");

        if (id is null || string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return new CardInfo {
            Id = id.Value,
            Name = name,
            Type = Text("type"),
            FrameType = Text("frameType"),
            Race = Text("race"),
            Attribute = Text("attribute"),
            Level = Number("level"),
            LinkVal = Number("linkval"),
            Scale = Number("scale"),
            Archetype = Text("archetype"),
            CanonicalCardId = fromCache ? Number("canonicalCardId") : Number("id"),
            ArtworkImageIds = ReadImageIds(),
            ArtworkMetadataKnown = !fromCache || (TryGetProperty(item, "artworkMetadataKnown", out JsonElement known) && known.ValueKind == JsonValueKind.True)
        };

        IReadOnlyList<int> ReadImageIds() {
            JsonElement images;
            bool hasArtworkImages = TryGetProperty(item, fromCache ? "artworkImageIds" : "card_images", out images) && images.ValueKind == JsonValueKind.Array;

            if (!hasArtworkImages) {
                return Array.Empty<int>();
            }

            List<int> result = new();

            foreach (JsonElement image in images.EnumerateArray()) {
                int imageId = default;

                if (fromCache) {
                    if (image.ValueKind != JsonValueKind.Number || !image.TryGetInt32(out imageId)) {
                        continue;
                    }
                }
                else {
                    bool hasValidImageId = image.ValueKind == JsonValueKind.Object &&
                        TryGetProperty(image, "id", out JsonElement imageIdValue) &&
                        imageIdValue.ValueKind == JsonValueKind.Number && imageIdValue.TryGetInt32(out imageId);

                    if (!hasValidImageId) {
                        continue;
                    }

                    bool hasExpectedImageUrl = TryGetProperty(image, "image_url_small", out JsonElement url) &&
                        url.ValueKind == JsonValueKind.String &&
                        url.GetString() == $"https://images.ygoprodeck.com/images/cards_small/{imageId}.jpg";

                    if (!hasExpectedImageUrl) {
                        continue;
                    }
                }

                if (imageId is > 0 and <= 2147483647 && !result.Contains(imageId)) {
                    result.Add(imageId);
                }
            }

            return result.Count == 0 ? Array.Empty<int>() : result.AsReadOnly();
        }

        string? Text(string key) => TryGetProperty(item, key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        int? Number(string key) => TryGetProperty(item, key, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number)
                ? number
                : null;
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement property) {
        if (element.TryGetProperty(name, out property)) {
            return true;
        }

        foreach (JsonProperty candidate in element.EnumerateObject()) {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
                property = candidate.Value;
                return true;
            }
        }

        property = default;

        return false;
    }
}
