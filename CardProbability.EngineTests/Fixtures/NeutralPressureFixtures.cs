using System.Text.Json;

namespace CardProbability.EngineTests.Fixtures;

// Owned mathematical snapshots retain the original multiplicities, predicates,
// encounter order and fixed-policy ordering. IDs were renamed in ordinal order.
// This test-local JSON shape is not an engine serialization contract.
internal static class NeutralPressureFixtures {
    internal static EngineCase Load(string name) {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "Data", name + ".json")));
        JsonElement root = document.RootElement;
        CalculationRequest request = new(
            root.GetProperty("Entries").EnumerateArray().Select(entry => new DeckEntry(
                entry.GetProperty("Id").GetString()!,
                entry.GetProperty("Copies").GetInt32(),
                Effect(entry.GetProperty("Effect")),
                Text(entry, "PolicyOrderKey"))),
            root.GetProperty("HandSize").GetInt32(),
            root.GetProperty("Goals").EnumerateArray().Select(goal => new Goal(
                goal.GetProperty("Id").GetString()!,
                goal.GetProperty("Requirements").EnumerateArray().Select(Requirement),
                goal.GetProperty("Alternatives").EnumerateArray().Select(alternative =>
                    new Alternative(alternative.GetProperty("Choices").EnumerateArray().Select(Requirement))),
                Text(goal, "GroupId"))),
            root.GetProperty("Memberships").EnumerateArray().Select(membership => new Membership(
                membership.GetProperty("Id").GetString()!,
                membership.GetProperty("EntryIds").EnumerateArray().Select(id => id.GetString()!))),
            root.GetProperty("Groups").EnumerateArray().Select(group => group.GetString()!));

        return new(name, request, new(CalculationMode.OptimisticSearchProjection, new(150_000_000)));
    }

    private static Requirement Requirement(JsonElement value) => new(
        (RequirementTargetKind)value.GetProperty("Kind").GetInt32(),
        value.GetProperty("Id").GetString()!,
        value.GetProperty("Minimum").GetInt32(),
        Integer(value, "Maximum"));

    private static EffectPlan? Effect(JsonElement value) {
        if (value.ValueKind == JsonValueKind.Null) {
            return null;
        }

        JsonElement search = value.GetProperty("Search");
        JsonElement excavation = value.GetProperty("Excavation");

        return new(
            Integer(value, "DrawCount"),
            Integer(value, "ActivationLimit"),
            value.GetProperty("HandCost").GetInt32(),
            Eligibility(value.GetProperty("HandCostEligibility")),
            value.GetProperty("HiddenDeckCost").GetInt32(),
            value.GetProperty("RemovalCount").GetInt32(),
            Eligibility(value.GetProperty("RemovalEligibility")),
            value.GetProperty("HiddenPostDrawMill").GetInt32(),
            value.GetProperty("FirstActivationOnly").GetBoolean(),
            value.GetProperty("BlocksLaterDraws").GetBoolean(),
            value.GetProperty("MinimumHandSize").GetInt32(),
            search.ValueKind == JsonValueKind.Null ? null : new SearchOperation(
                Eligibility(search.GetProperty("Targets"))!,
                search.GetProperty("Modes").EnumerateArray().Select(mode => new SearchMode(
                    Eligibility(mode.GetProperty("Targets"))!, Integer(mode, "ActivationLimit")))),
            excavation.ValueKind == JsonValueKind.Null ? null : new ExcavationOperation(
                excavation.GetProperty("Count").GetInt32(), excavation.GetProperty("RequiresNoPriorEffectDraw").GetBoolean())
        );
    }

    private static Eligibility? Eligibility(JsonElement value) => value.ValueKind == JsonValueKind.Null
        ? null : new(Text(value, "MembershipId"));

    private static string? Text(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : value.GetProperty(name).GetString();

    private static int? Integer(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : value.GetProperty(name).GetInt32();
}
