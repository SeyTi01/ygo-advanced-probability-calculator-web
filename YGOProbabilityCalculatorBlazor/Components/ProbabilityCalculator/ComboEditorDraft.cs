using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal enum ComboDraftTargetKind { Category, Card }

internal readonly record struct ComboDraftTarget(ComboDraftTargetKind Kind, string Identity) {
    public string CategoryIdentity => Kind == ComboDraftTargetKind.Category ? Identity : string.Empty;
    public string CardId => Kind == ComboDraftTargetKind.Card ? Identity : string.Empty;
    public string PickerValue => Kind == ComboDraftTargetKind.Card && Identity.Length > 0 ? $"card:{Identity}" : Identity;

    public static ComboDraftTarget Category(string identity) => new(ComboDraftTargetKind.Category, identity);
    public static ComboDraftTarget Card(string id) => new(ComboDraftTargetKind.Card, id);
}

// Index identifies the exact occurrence, including repeated object references in
// legacy sessions. GroupIndex distinguishes an alternative from a top-level row.
internal readonly record struct ComboDraftLocation(ComboRequirementKind Kind, int Index, int? GroupIndex = null) {
    public static ComboDraftLocation For(ComboAlternative requirement, int index, int? groupIndex) =>
        new(requirement.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card, index, groupIndex);

    public ComboAlternative? Find(Combo combo) {
        if (Index < 0) { return null; }
        if (GroupIndex is int group) {
            return group < 0 ? null : combo.AlternativeGroups.ElementAtOrDefault(group)?.Alternatives.ElementAtOrDefault(Index);
        }
        if (Kind == ComboRequirementKind.Category && combo.Categories.ElementAtOrDefault(Index) is { } category) {
            return ComboAlternative.For(category);
        }
        if (Kind == ComboRequirementKind.Card && combo.Cards.ElementAtOrDefault(Index) is { } card) {
            return ComboAlternative.For(card);
        }

        return null;
    }

    public ComboRequirementPosition Position => GroupIndex is int group
        ? new(ComboRequirementKind.Group, group) : new(Kind, Index);
}

internal sealed class ComboDraftValues {
    public ComboDraftTarget Target { get; set; } = ComboDraftTarget.Category(string.Empty);
    public string Minimum { get; set; } = "1";
    public string Maximum { get; set; } = string.Empty;
    public int ActiveProperty { get; set; }
    public List<CategoryBase> Properties { get; } = [];

    public void RenameCategory(string oldIdentity, string newIdentity) {
        if (Target.CategoryIdentity == oldIdentity) { Target = ComboDraftTarget.Category(newIdentity); }
    }
}

internal abstract class ComboDraftOperation { }
internal sealed class ComboNewRequirementDraft : ComboDraftOperation { }

internal sealed class ComboExistingRequirementDraft(
    ComboAlternative requirement,
    ComboDraftLocation location
) : ComboDraftOperation {
    public ComboAlternative Requirement { get; set; } = requirement;
    public ComboDraftLocation Location { get; set; } = location;
}

internal abstract record ComboAlternativeAnchor;
internal sealed record ComboAlternativeSeedAnchor(ComboAlternative Seed, ComboDraftLocation Location) : ComboAlternativeAnchor;
internal sealed record ComboAlternativeGroupAnchor(ComboAlternativeGroup Group, int GroupIndex) : ComboAlternativeAnchor;

internal sealed class ComboAlternativeDraft(ComboAlternativeAnchor anchor) : ComboDraftOperation {
    public ComboAlternativeAnchor Anchor { get; set; } = anchor;
    public int? InsertIndex { get; set; }
    public object? TermIdentity { get; set; }
}

internal sealed class ComboPropertyDraft(
    ComboCategory seed,
    ComboDraftLocation location,
    ComboAlternativeGroup? group
) : ComboDraftOperation {
    public ComboCategory Seed { get; set; } = seed;
    public ComboDraftLocation Location { get; } = location;
    public ComboAlternativeGroup? Group { get; set; } = group;
}

internal sealed class ComboEditorDraft {
    public ComboDraftOperation? Operation { get; private set; }
    public ComboDraftValues Values { get; private set; } = new();
    public ComboDraftValues? SuspendedValues { get; private set; }
    public ComboExistingRequirementDraft? Editing => Operation as ComboExistingRequirementDraft;
    public ComboAlternativeDraft? Alternative => Operation as ComboAlternativeDraft;
    public ComboPropertyDraft? Property => Operation as ComboPropertyDraft;
    public bool IsNewRequirement => Operation is ComboNewRequirementDraft;
    public bool IsAuthoring => Operation is ComboAlternativeDraft or ComboPropertyDraft;

    public void BeginNewRequirement() => Operation = new ComboNewRequirementDraft();
    public void FinishNewRequirement() => Operation = null;

    public void BeginEdit(ComboAlternative requirement, ComboDraftLocation location, int property) {
        SuspendedValues = Values;
        Values = new() { ActiveProperty = property };
        Operation = new ComboExistingRequirementDraft(requirement, location);
        SetBounds(requirement);
        if (requirement.Category is { } category) {
            Values.Properties.AddRange(category.CategoryBases);
            Values.Target = ComboDraftTarget.Category(Values.Properties[property].Identity);
        }
        else {
            Values.Target = ComboDraftTarget.Card(requirement.Card!.CardId);
        }
    }

    // Extending an edited row keeps the original suspended add draft through the
    // intermediate accepted parent update, rather than suspending the row again.
    public void ReleaseEditedRow() => Operation = null;

    public void BeginAlternative(ComboAlternativeAnchor anchor) {
        SuspendedValues ??= Values;
        Values = new() { Target = new(Values.Target.Kind, string.Empty), ActiveProperty = Values.ActiveProperty };
        Operation = new ComboAlternativeDraft(anchor);
    }

    public void BeginProperty(ComboCategory seed, ComboDraftLocation location, ComboAlternativeGroup? group) {
        SuspendedValues ??= Values;
        Values = new() { ActiveProperty = Values.ActiveProperty };
        Operation = new ComboPropertyDraft(seed, location, group);
        SetBounds(ComboAlternative.For(seed));
    }

    public void Cancel() {
        Values = SuspendedValues ?? new() { Target = new(Values.Target.Kind, string.Empty) };
        SuspendedValues = null;
        Operation = null;
    }

    private void SetBounds(ComboAlternative requirement) {
        Values.Minimum = (requirement.Category?.MinCount ?? requirement.Card!.MinCount).ToString();
        RequirementMaximumMode mode = requirement.Category?.MaximumMode ?? requirement.Card!.MaximumMode;
        Values.Maximum = mode == RequirementMaximumMode.HandSize ? string.Empty
            : (requirement.Category?.MaxCount ?? requirement.Card!.MaxCount).ToString();
    }
}
