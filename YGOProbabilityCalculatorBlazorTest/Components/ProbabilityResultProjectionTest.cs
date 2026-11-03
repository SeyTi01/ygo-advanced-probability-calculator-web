using System.Globalization;
using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
[SetCulture("en-US")]
public sealed class ProbabilityResultProjectionTest {
    [Test]
    public void FilteredGroupsAndReorderedCombosRetainExactFractionIndicesAcrossPanelsAndExport() {
        CalculationResultContract contract = ProbabilityPresentationTest.Contract(CalculationAccuracy.Exact) with {
            Fractions = new(new("3", "4"), [new("1", "4"), new("1", "2"), new("3", "4")],
                [new("0", "1"), new("3", "4")])
        };
        ProbabilityCalculationResult result = new(.75,
            [new(2, "First displayed", .75, "visible"), new(0, null, .25, "hidden"), new(1, "Orphan", .5, "missing")],
            [new("hidden", "Hidden group", 0, 0), new("visible", "Visible group", .75, 1)], Contract: contract);
        ProbabilityResultProjection projection = ProbabilityResultProjection.Create(result);
        Assert.That(projection.Groups.Single().Index, Is.EqualTo(1));
        Assert.That(projection.Groups.Single().Members.Select(combo => combo.Index), Is.EqualTo(new[] { 2 }));
        Assert.That(projection.Ungrouped.Select(combo => combo.Index), Is.EqualTo(new[] { 0, 1 }));

        using TestContext context = new();
        IRenderedComponent<ProbabilityResultBody> current = context.RenderComponent<ProbabilityResultBody>(parameters => parameters
            .Add(body => body.Projection, projection)
            .Add(body => body.Contract, contract));
        PinnedResultSnapshot snapshot = PinnedResultSnapshot.Capture(new(1, 4, 3), result);
        IRenderedComponent<PinnedResultPanel> pinned = context.RenderComponent<PinnedResultPanel>(parameters => parameters.Add(panel => panel.Snapshot, snapshot));

        foreach (IRenderedFragment panel in new IRenderedFragment[] { current, pinned }) {
            ProbabilityValue[] values = panel.FindComponents<ProbabilityValue>().Select(component => component.Instance).ToArray();
            Assert.That(values.Select(value => (value.Row, value.Index)), Is.EqualTo(new[] {
                (ProbabilityRow.Total, 0), (ProbabilityRow.Group, 1), (ProbabilityRow.Combo, 2),
                (ProbabilityRow.Combo, 0), (ProbabilityRow.Combo, 1)
            }));
            Assert.That(panel.FindAll("[role='note']").Select(note => note.GetAttribute("title")),
                Is.EqualTo(new[] { "Modeled primary fraction: 3/4", "Modeled primary fraction: 3/4", "Modeled primary fraction: 3/4",
                    "Modeled primary fraction: 1/4", "Modeled primary fraction: 1/2" }));
        }

        string summary = ProbabilityResultSummaryFormatter.Format(result, 1, CultureInfo.InvariantCulture);
        Assert.That(summary, Does.Contain("Exact modeled fraction, group 2: 3/4").And.Not.Contain("Exact modeled fraction, group 1:"));
        Assert.That(summary, Does.Contain($"**First displayed** — {.75.ToString("P2", CultureInfo.InvariantCulture)} ±0%")
            .And.Contain($"**Unnamed combo 1** — {.25.ToString("P2", CultureInfo.InvariantCulture)} ±0%"));
    }

    [Test]
    public void PinnedProjectionKeepsCapturedLabelsMembershipAndValuesAfterSourceListsChange() {
        List<ComboProbabilityResult> combos = [new(0, "Captured route", .5, "group")];
        List<GroupProbabilityResult> groups = [new("group", "Captured group", .5, 1)];
        ProbabilityCalculationResult result = new(.5, combos, groups);
        string capturedExport = ProbabilityResultSummaryFormatter.Format(result, 1, CultureInfo.InvariantCulture);
        PinnedResultSnapshot snapshot = PinnedResultSnapshot.Capture(new(1, 4, 2), result);
        using TestContext context = new();
        IRenderedComponent<PinnedResultPanel> pinned = context.RenderComponent<PinnedResultPanel>(parameters => parameters.Add(panel => panel.Snapshot, snapshot));

        combos[0] = new(0, "Changed route", .9);
        groups[0] = new("group", "Changed group", .9, 0);
        pinned.SetParametersAndRender(parameters => parameters.Add(panel => panel.Snapshot, snapshot));

        ProbabilityResultProjection frozen = ProbabilityResultProjection.Create(snapshot);
        Assert.That(frozen.Groups.Single().Label, Is.EqualTo("Captured group"));
        Assert.That(frozen.Groups.Single().Members.Single().Label, Is.EqualTo("Captured route"));
        Assert.That(frozen.Groups.Single().Members.Single().Probability, Is.EqualTo(.5));
        Assert.That(frozen.Ungrouped, Is.Empty);
        Assert.That(pinned.Markup, Does.Contain("Captured group").And.Contain("Captured route").And.Not.Contain("Changed"));
        string capturedValue = .5.ToString("P2", CultureInfo.InvariantCulture);
        Assert.That(capturedExport, Does.Contain($"**Captured group** — {capturedValue}").And.Contain($"**Captured route** — {capturedValue}"));
        string currentExport = ProbabilityResultSummaryFormatter.Format(result, 1, CultureInfo.InvariantCulture);
        Assert.That(currentExport, Does.Contain($"**Changed route** — {.9.ToString("P2", CultureInfo.InvariantCulture)}").And.Not.Contain("Captured"));
        Assert.That(ProbabilityResultProjection.Create(result).Ungrouped.Single().Label, Is.EqualTo("Changed route"));
    }
}
