using System.Collections.Immutable;

namespace CardProbability;

/// <summary>An owned, immutable problem. IDs are opaque, ordinal and case-sensitive.</summary>
public sealed class CalculationRequest {
    /// <summary>Physical-copy classes with unique entry IDs; zero-copy entries are allowed.</summary>
    public ImmutableArray<DeckEntry> Entries { get; }
    /// <summary>Resolved, possibly overlapping predicates with unique membership IDs.</summary>
    public ImmutableArray<Membership> Memberships { get; }
    /// <summary>Goals whose overlap-safe union forms the total result.</summary>
    public ImmutableArray<Goal> Goals { get; }
    /// <summary>Unique reporting group IDs referenced by Goal.GroupId.</summary>
    public ImmutableArray<string> Groups { get; }
    /// <summary>Number of physical copies dealt before effects, between zero and the deck's total copies.</summary>
    public int HandSize { get; }

    /// <summary>Snapshots the supplied collections. Semantic validation occurs when calculating.</summary>
    /// <param name="entries">Non-null entries; total multiplicity must fit Int32.MaxValue.</param>
    /// <param name="handSize">Opening hand size.</param>
    /// <param name="goals">Non-null goals with unique IDs.</param>
    /// <param name="memberships">Optional resolved predicates, including explicitly empty sets.</param>
    /// <param name="groups">Optional reporting groups; separate from Goal.Alternatives.</param>
    /// <remarks>IDs must be nonblank and unique within each ID space. Do not mutate enumerables during construction.</remarks>
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
/// <param name="Id">Opaque, nonblank entry identity.</param>
/// <param name="Copies">Nonnegative number of physical copies.</param>
/// <param name="Effect">One declared operation, or null for an ordinary entry.</param>
/// <param name="PolicyOrderKey">Advanced deterministic keep/removal ordering key. Supply keys for all entries or none; empty and equal keys are legal.</param>
/// <remarks>Keys compare ordinally before ordinary/effect-source and entry-ID tie-breaks. Omitted keys derive from
/// requirement membership. Changing keys can change outcomes when costs or removals execute; they do not set activation priority.</remarks>
public sealed record DeckEntry(string Id, int Copies, EffectPlan? Effect = null, string? PolicyOrderKey = null);

/// <summary>A resolved predicate, including the explicitly empty set. No metadata is interpreted.</summary>
public sealed class Membership {
    /// <summary>Opaque, nonblank membership identity.</summary>
    public string Id { get; }
    /// <summary>Unique declared entry IDs satisfying this predicate.</summary>
    public ImmutableArray<string> EntryIds { get; }

    /// <summary>Copies a predicate's resolved entry set; the engine infers no metadata or intersections.</summary>
    /// <param name="id">Membership ID.</param>
    /// <param name="entryIds">Non-null set of entry IDs; an empty set is valid.</param>
    public Membership(string id, IEnumerable<string> entryIds) {
        Id = id;
        EntryIds = entryIds.ToImmutableArray();
    }
}

/// <summary>The ID space referenced by a requirement.</summary>
public enum RequirementTargetKind {
    /// <summary>A resolved predicate over entries.</summary>
    Membership,
    /// <summary>One declared entry.</summary>
    Entry
}

/// <summary>Positive minima of different targets require distinct copies. Repeated identical targets intersect bounds.</summary>
/// <param name="Kind">Target ID space.</param>
/// <param name="Id">Declared entry or membership ID.</param>
/// <param name="Minimum">Inclusive nonnegative minimum; positive slots need distinct physical copies.</param>
/// <param name="Maximum">Inclusive nonnegative maximum over all matching held copies, including those filling another target. Null means the evaluated hand size.</param>
/// <remarks>A 0-to-0 requirement excludes matches. Minimum above maximum is valid but impossible.</remarks>
public sealed record Requirement(RequirementTargetKind Kind, string Id, int Minimum = 1, int? Maximum = null) {
    /// <summary>Requires physical copies from a declared membership.</summary>
    /// <param name="id">Membership ID.</param>
    /// <param name="minimum">Inclusive minimum, using distinct copies across different targets.</param>
    /// <param name="maximum">Hand-wide inclusive maximum, or null for hand size.</param>
    /// <returns>A membership requirement.</returns>
    public static Requirement InMembership(string id, int minimum = 1, int? maximum = null) => new(RequirementTargetKind.Membership, id, minimum, maximum);
    /// <summary>Requires physical copies of a declared entry.</summary>
    /// <param name="id">Entry ID.</param>
    /// <param name="minimum">Inclusive minimum, using distinct copies across different targets.</param>
    /// <param name="maximum">Hand-wide inclusive maximum, or null for hand size.</param>
    /// <returns>An entry requirement.</returns>
    public static Requirement OfEntry(string id, int minimum = 1, int? maximum = null) => new(RequirementTargetKind.Entry, id, minimum, maximum);
}

/// <summary>OR of complete requirements; separate Alternative groups within a goal are ANDed.</summary>
/// <remarks>This is goal logic. Goal.GroupId instead assigns a goal to a reporting union.</remarks>
public sealed class Alternative {
    /// <summary>Nonempty choices, any one of which can satisfy this group.</summary>
    public ImmutableArray<Requirement> Choices { get; }

    /// <summary>Snapshots one OR group's requirements.</summary>
    /// <param name="choices">At least one requirement; both bounds of the selected choice must hold.</param>
    public Alternative(IEnumerable<Requirement> choices) {
        Choices = choices.ToImmutableArray();
    }
}

/// <summary>AND of required requirements and alternative groups; each goal is evaluated independently.</summary>
/// <remarks>All positive slots in a chosen route require distinct copies across different targets. Search choices may
/// differ between goals; all rows share the keep/removal policy derived from the entire active goal set.</remarks>
public sealed class Goal {
    /// <summary>Opaque, nonblank goal identity.</summary>
    public string Id { get; }
    /// <summary>Requirements that must all hold.</summary>
    public ImmutableArray<Requirement> Requirements { get; }
    /// <summary>ANDed groups, each containing OR choices.</summary>
    public ImmutableArray<Alternative> Alternatives { get; }
    /// <summary>Optional declared reporting group; its result is the union of assigned goals, not an AND condition.</summary>
    public string? GroupId { get; }

    /// <summary>Snapshots a goal's logic and optional reporting assignment.</summary>
    /// <param name="id">Goal ID.</param>
    /// <param name="requirements">Non-null required conditions; may be empty.</param>
    /// <param name="alternatives">Optional OR groups, all of which must hold.</param>
    /// <param name="groupId">Declared reporting group ID, or null.</param>
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
/// <param name="MembershipId">Declared membership ID, or null for every entry.</param>
public sealed record Eligibility(string? MembershipId = null) {
    /// <summary>Unrestricted eligibility over all entries.</summary>
    public static Eligibility Any { get; } = new();
}

/// <summary>One optional search choice; each activation chooses one mode.</summary>
/// <param name="Targets">Non-null target eligibility.</param>
/// <param name="ActivationLimit">Positive per-mode allowance shared by all copies of the source entry, or null for no extra mode cap. The enclosing effect limit still applies.</param>
public sealed record SearchMode(Eligibility Targets, int? ActivationLimit = null);

/// <summary>Optionally consume a held source to transfer one eligible remaining deck copy into the hand.</summary>
/// <remarks>Only sources held before the excavation addition can search. Fetched effects never activate: the model
/// has one search pass, with the draw and excavation phases already closed. Search choices are existence tests, not callbacks.</remarks>
public sealed class SearchOperation {
    /// <summary>Non-null eligibility for all possible targets; must equal the set union of Modes when supplied.</summary>
    public Eligibility Targets { get; }
    /// <summary>Optional mode choices and their shared per-entry allowances; empty means a single unrestricted-by-mode search.</summary>
    public ImmutableArray<SearchMode> Modes { get; }

    /// <summary>Snapshots search modes. Their target union is compared as resolved entry sets, including zero-copy entries.</summary>
    /// <param name="targets">Non-null eligibility; use Eligibility.Any for all entries.</param>
    /// <param name="modes">Optional nonempty mode set, or null/empty for no mode restrictions.</param>
    public SearchOperation(Eligibility targets, IEnumerable<SearchMode>? modes = null) {
        Targets = targets;
        Modes = modes?.ToImmutableArray() ?? [];
    }
}

/// <summary>Reveal a fixed-size pool and keep exactly one copy, after draws and before ordinary searches.</summary>
/// <param name="Count">Positive reveal count; the full pool must fit in the remaining deck.</param>
/// <param name="RequiresNoPriorEffectDraw">Disable this source on branches where an effect has already drawn cards.</param>
/// <remarks>At most one excavation activates. The eligible held source with the largest Count wins, then ordinal
/// entry ID breaks ties. It is consumed even when no reveal helps. Unselected copies remain searchable.
/// The kept effect never activates, and creates no new search opportunity.</remarks>
public sealed record ExcavationOperation(int Count, bool RequiresNoPriorEffectDraw = false);

/// <summary>Closed instruction set: draw, or search one, or excavate and keep one. No execution callbacks.</summary>
/// <param name="DrawCount">Positive draw batch size, or null when choosing Search or Excavation.</param>
/// <param name="ActivationLimit">Positive entry-wide allowance shared by its physical copies; null removes the cap, but each activation still consumes a source. Excavation requires 1.</param>
/// <param name="HandCost">Nonnegative number of eligible other held copies removed before a draw/search activation.</param>
/// <param name="HandCostEligibility">Null or Eligibility.Any accepts any other copy. A membership filter requires positive HandCost.</param>
/// <param name="HiddenDeckCost">Nonnegative hidden top-deck removal before drawing; draw only.</param>
/// <param name="RemovalCount">Nonnegative mandatory hand removal after drawing/searching. Failure loses that branch without renormalizing.</param>
/// <param name="RemovalEligibility">Null or Eligibility.Any accepts any held copy after source consumption. A membership filter requires positive RemovalCount.</param>
/// <param name="HiddenPostDrawMill">Nonnegative hidden top-deck removal after the draw batch; draw only.</param>
/// <param name="FirstActivationOnly">Draw only: this source can activate only before any draw effect has activated.</param>
/// <param name="BlocksLaterDraws">Draw only: after this activation, no later draw effect may activate.</param>
/// <param name="MinimumHandSize">Nonnegative minimum held-copy count before activation, including the source; draw/search only.</param>
/// <param name="Search">Search operation, mutually exclusive with DrawCount and Excavation.</param>
/// <param name="Excavation">Standalone excavation, mutually exclusive with DrawCount and Search.</param>
/// <remarks>
/// Set exactly one operation; use a null DeckEntry.Effect for no operation. Draws may combine all draw fields,
/// hand costs, removals and MinimumHandSize. Searches allow hand costs, removals, MinimumHandSize and entry/mode
/// allowances, but no hidden deck costs or draw sequencing flags. Excavation requires ActivationLimit = 1,
/// zero costs/removals/MinimumHandSize and false draw flags. Counts must be nonnegative; supplied draw counts
/// and activation limits must be positive. Every filter reference is validated, even in OpeningHand mode.
/// Legal automatic draws consume the source, pay costs, draw the entire batch, mill, then remove held copies.
/// Costs cannot use the activating source. The complete deck cost, draw and mill must fit before activation;
/// an unavailable source stays held without spending its allowance. Newly drawn effects may activate.
/// Resolution order is draws, at most one excavation, then optional searches. Searched/excavated effects do
/// not reactivate earlier phases or create new search opportunities. Keep/removal choices are deterministic
/// and depend on the full goal set; they do not optimize separately for each result row.
/// </remarks>
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
