using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestDrafts : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExtendingAnAcceptedRowEditRestoresTheOriginalSuspendedDraft(bool sameCard) {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [new([], name: "Draft card", id: "draft")],
            Combos = [new([new(a, 1, 5)])],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = CardTarget("draft") });
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "0" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "0" });

        await EditRequirement(editor, "category:0");
        await RequirementField(editor, "#minCount0").InputAsync(new() { Value = "2" });
        await RequirementField(editor, "#maxCount0").InputAsync(new() { Value = "2" });
        await editor.Find(sameCard ? "[data-action='property']" : "[data-action='alternative']").ClickAsync(new());
        Combo accepted = editor.Instance.Combo;
        Assert.That(accepted.Categories[0].MinCount, Is.EqualTo(2));
        Assert.That(accepted.Categories[0].MaxCount, Is.EqualTo(2));

        await editor.Find("#comboName0").InputAsync(new() { Value = "Renamed while extending" });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "4" });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = b.Identity });
        await Button(editor, "Cancel").ClickAsync(new());

        Assert.That(editor.Instance.Combo.Name, Is.EqualTo("Renamed while extending"));
        Assert.That(editor.Instance.Combo.Categories[0], Is.SameAs(accepted.Categories[0]));
        Assert.That(editor.Instance.Combo.AlternativeGroups, Is.Empty);
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.EqualTo(CardTarget("draft")));
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(RequirementField(editor, "#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        await Button(editor, "Add").ClickAsync(new());
        Assert.That(editor.Instance.Combo.Cards.Single().CardId, Is.EqualTo("draft"));
        Assert.That(editor.Instance.Combo.Cards.Single().MinCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Cards.Single().MaxCount, Is.Zero);
        Assert.That(editor.Instance.Combo.Cards.Single().MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
    }

    [Test]
    public async Task ExtendingARepeatedZeroOccurrenceKeepsAuthoredOrderAndUntouchedDefaults() {
        ComboCategory repeated = new(a, 0, 0);
        ComboCategory sibling = new(b, 1, 5);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a, b],
            Cards = [new([], name: "Alternative", id: "alternative")],
            Combos = [new([repeated, repeated, sibling])],
            HandSize = 5
        });
        IRenderedComponent<ComboEditor> editor = cut.FindComponent<ComboEditor>();
        await ExtendRequirement(editor, "category:1");
        await editor.Find("#comboName0").InputAsync(new() { Value = "Unrelated edit" });
        await cut.Find("#handSize").ChangeAsync(new() { Value = "4" });
        await RequirementField(editor, "#comboTarget0").ChangeAsync(new() { Value = CardTarget("alternative") });
        await Button(editor, "Add alternative").ClickAsync(new());

        Combo updated = editor.Instance.Combo;
        Assert.That(updated.Categories, Is.EqualTo(new[] { repeated, sibling }));
        ComboAlternativeGroup group = updated.AlternativeGroups.Single();
        Assert.That(group.Alternatives[0].Category, Is.SameAs(repeated));
        Assert.That(group.Alternatives[0].Category!.MinCount, Is.Zero);
        Assert.That(group.Alternatives[0].Category!.MaxCount, Is.Zero);
        Assert.That(group.Alternatives[1].Card!.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(group.Alternatives[1].Card!.MaxCount, Is.EqualTo(4));
        Assert.That(updated.RequirementOrder, Is.EqualTo(new[] {
            new ComboRequirementPosition(ComboRequirementKind.Category, 0),
            new ComboRequirementPosition(ComboRequirementKind.Group, 0),
            new ComboRequirementPosition(ComboRequirementKind.Category, 1)
        }));
        Assert.That(RequirementField(editor, "#comboTarget0").GetAttribute("value"), Is.Empty);
        Assert.That(RequirementField(editor, "#minCount0").GetAttribute("value"), Is.EqualTo("1"));
        AssertAnyMaximumDraft(editor);
    }
}
