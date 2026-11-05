using System.Collections.Immutable;

namespace CardProbability;

/// <summary>An owned, immutable problem. IDs are opaque, ordinal and case-sensitive.</summary>
public sealed class CalculationRequest {
    public ImmutableArray<DeckEntry> Entries { get; }
    public ImmutableArray<Membership> Memberships { get; }
    public ImmutableArray<Goal> Goals { get; }
    public ImmutableArray<string> Groups { get; }
    public int HandSize { get; }

    public CalculationRequest(
        IEnumerable<DeckEntry> entries,
        int handSize,
        IEnumerable<Goal> goals,
        IEnumerable<Membership>? memberships = null,
        IEnumerable<string>? groups = null
    ) {
        Entries = entries.ToImmutableArray();
        HandSize = handSize;
        Goals = goals.ToImmutableArray();
        Memberships = memberships?.ToImmutableArray() ?? [];
        Groups = groups?.ToImmutableArray() ?? [];
    }
}

/// <summary>Copies share this entry's activation allowance; different entries never share it.</summary>
public sealed record DeckEntry(string Id, int Copies, EffectPlan? Effect = null, string? PolicyOrderKey = null);

/// <summary>A resolved predicate, including the explicitly empty set. No metadata is interpreted.</summary>
public sealed class Membership {
    public string Id { get; }
    public ImmutableArray<string> EntryIds { get; }

    public Membership(string id, IEnumerable<string> entryIds) {
        Id = id;
        EntryIds = entryIds.ToImmutableArray();
    }
}

public enum RequirementTargetKind { Membership, Entry }

/// <summary>Positive minima of different targets require distinct copies. Repeated identical targets intersect bounds.</summary>
public sealed record Requirement(RequirementTargetKind Kind, string Id, int Minimum = 1, int? Maximum = null) {
    public static Requirement InMembership(string id, int minimum = 1, int? maximum = null) => new(RequirementTargetKind.Membership, id, minimum, maximum);
    public static Requirement OfEntry(string id, int minimum = 1, int? maximum = null) => new(RequirementTargetKind.Entry, id, minimum, maximum);
}

/// <summary>One of these complete requirements must hold. Different groups are conjoined.</summary>
public sealed class Alternative {
    public ImmutableArray<Requirement> Choices { get; }

    public Alternative(IEnumerable<Requirement> choices) {
        Choices = choices.ToImmutableArray();
    }
}

public sealed class Goal {
    public string Id { get; }
    public ImmutableArray<Requirement> Requirements { get; }
    public ImmutableArray<Alternative> Alternatives { get; }
    public string? GroupId { get; }

    public Goal(
        string id,
        IEnumerable<Requirement> requirements,
        IEnumerable<Alternative>? alternatives = null,
        string? groupId = null
    ) {
        Id = id;
        Requirements = requirements.ToImmutableArray();
        Alternatives = alternatives?.ToImmutableArray() ?? [];
        GroupId = groupId;
    }
}

/// <summary>Null selects every entry. Otherwise selects a declared membership.</summary>
public sealed record Eligibility(string? MembershipId = null) {
    public static Eligibility Any { get; } = new();
}

public sealed record SearchMode(Eligibility Targets, int? ActivationLimit = null);

public sealed class SearchOperation {
    public Eligibility Targets { get; }
    public ImmutableArray<SearchMode> Modes { get; }

    public SearchOperation(Eligibility targets, IEnumerable<SearchMode>? modes = null) {
        Targets = targets;
        Modes = modes?.ToImmutableArray() ?? [];
    }
}

public sealed record ExcavationOperation(int Count, bool RequiresNoPriorEffectDraw = false);

/// <summary>Closed instruction set: draw, or search one, or excavate and keep one. No execution callbacks.</summary>
public sealed record EffectPlan(
    int? DrawCount = null,
    int? ActivationLimit = 1,
    int HandCost = 0,
    Eligibility? HandCostEligibility = null,
    int HiddenDeckCost = 0,
    int RemovalCount = 0,
    Eligibility? RemovalEligibility = null,
    int HiddenPostDrawMill = 0,
    bool FirstActivationOnly = false,
    bool BlocksLaterDraws = false,
    int MinimumHandSize = 0,
    SearchOperation? Search = null,
    ExcavationOperation? Excavation = null
);
