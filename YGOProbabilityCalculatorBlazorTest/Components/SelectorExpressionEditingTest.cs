using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class SelectorExpressionEditingTest {
    private static CardSelector Property(string name) => new(CardSelectorKind.Category, [$"user:{name}"]);

    [Test]
    public void ProjectionKeepsNestedBoundariesAndDirectOperandOrderWithoutChangingTheModel() {
        CardSelector first = Property("Z");
        CardSelector second = Property("A");
        CardSelector nested = CardSelector.AnyOfSelectors([Property("D"), Property("C")]);
        CardSelector last = CardSelector.LevelAtLeast(5);
        CardSelector selector = CardSelector.AllOfSelectors([first, second, nested, last]);
        string originalJson = JsonSerializer.Serialize(selector);

        SelectorRow[] rows = SelectorExpressionEditing.ProjectRows(selector.Kind, selector.AuthoredTerms.ToArray());

        Assert.That(rows.Select(row => row.Terms.Length), Is.EqualTo(new[] { 2, 1, 1 }));
        Assert.That(rows[0].Value.AuthoredTerms, Is.EqualTo(new[] { first, second }));
        Assert.That(rows[1].Value, Is.SameAs(nested));
        Assert.That(rows[2].Value, Is.SameAs(last));
        Assert.That(JsonSerializer.Serialize(selector), Is.EqualTo(originalJson));
    }

    [Test]
    public void OperatorRoundTripRetainsSeparatelyAuthoredRowsAfterFlattening() {
        CardSelector first = CardSelector.AnyOfSelectors([Property("Z"), Property("A")]);
        CardSelector second = CardSelector.AnyOfSelectors([Property("D"), Property("C")]);
        CardSelector selector = CardSelector.AllOfSelectors([first, second]);
        SelectorRow[] originalRows = SelectorExpressionEditing.ReconcileRows(selector, [], null);

        SelectorOperatorChange? forward = SelectorExpressionEditing.ChangeOperator(selector, originalRows, CardSelectorKind.AnyOf);
        Assert.That(forward, Is.Not.Null);
        SelectorRow[] retained = SelectorExpressionEditing.ReconcileRows(forward!.Updated, originalRows, originalRows);
        Assert.That(retained.Select(row => row.Value), Is.EqualTo(new[] { first, second }));

        SelectorOperatorChange? backward = SelectorExpressionEditing.ChangeOperator(forward.Updated, retained, CardSelectorKind.AllOf);
        Assert.That(backward, Is.Not.Null);
        Assert.That(OperatorReversibility.SameExpression(selector, backward!.Updated), Is.True);
    }

    [Test]
    public void NestedOperatorChangeRejectsLosingEqualSiblingOperands() {
        CardSelector first = Property("A");
        CardSelector second = Property("B");
        CardSelector nested = CardSelector.AnyOfSelectors([second, first]);
        CardSelector selector = CardSelector.AllOfSelectors([nested, first, second]);
        SelectorRow[] rows = SelectorExpressionEditing.ReconcileRows(selector, [], null);
        CardSelector updated = CardSelector.AllOfSelectors([second, first]);

        Assert.That(SelectorExpressionEditing.ReplaceRowOperator(selector, rows, 0, updated, nested), Is.Null);
    }

    [Test]
    public void ParentReconciliationKeepsDraftChangesAndErrorsAndAddsNewSiblingsInOrder() {
        CardSelector first = Property("A");
        CardSelector removedLocally = Property("B");
        CardSelector deletedByParent = Property("C");
        CardSelector addedByParent = CardSelector.LevelAtLeast(6);
        SelectorOperandDraft edited = new(first) { Identity = "user:Draft", Error = "invalid raw input" };
        SelectorOperandDraft deleted = new(deletedByParent);

        SelectorOperandDraft[] refreshed = SelectorExpressionEditing.ReconcileOperands(
            [edited, deleted],
            [first, removedLocally, deletedByParent],
            [addedByParent, first, removedLocally]
        );

        Assert.That(refreshed, Has.Length.EqualTo(2));
        Assert.That(refreshed[0].Build(), Is.SameAs(addedByParent));
        Assert.That(refreshed[1], Is.SameAs(edited));
        Assert.That(refreshed[1].Identity, Is.EqualTo("user:Draft"));
        Assert.That(refreshed[1].Error, Is.EqualTo("invalid raw input"));
    }

    [Test]
    public void DraftReconciliationConsumesEachMatchingOccurrenceOnce() {
        CardSelector term = Property("A");
        SelectorOperandDraft first = new(term) { Identity = "user:First" };
        SelectorOperandDraft second = new(term) { Identity = "user:Second" };

        SelectorOperandDraft[] refreshed = SelectorExpressionEditing.ReconcileOperands([first, second], [term, term], [term, term]);

        Assert.That(refreshed[0], Is.SameAs(first));
        Assert.That(refreshed[1], Is.SameAs(second));
    }
}
