namespace YGOProbabilityCalculatorBlazor.Models;

// A search transfers exactly one remaining Main Deck copy to hand and consumes
// its source. No OPT is implied. Selectors never depend on a target display name.
public sealed record SearchEffectPlan(CardSelector Selector, bool ObjectiveMetadataOnly = true) {
    public bool Matches(Card card) {
        if (!card.Active || card.Copies <= 0) {
            return false;
        }
        if (!ObjectiveMetadataOnly) {
            return Selector.Matches(card.Categories);
        }

        return card.ExternalCardId is > 0 && Selector.Matches(card.Categories
            .Where(category => category.Source == CategorySource.Metadata
                && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)).ToArray());
    }
}
