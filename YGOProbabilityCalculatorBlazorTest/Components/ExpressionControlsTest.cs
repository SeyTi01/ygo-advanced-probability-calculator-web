using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class ExpressionControlsTest : CalculatorEditorTestBase {
    [Test]
    public void AddingAlternativeToCompositeShowsGroupingAndKeepsEachPropertyEditable() {
        YGOProbabilityCalculatorBlazor.Models.CategoryBase third = new("C");
        YGOProbabilityCalculatorBlazor.Models.Combo current = new([
            YGOProbabilityCalculatorBlazor.Models.ComboCategory.SameCard([a, b], 1, 3)
        ]);
        IRenderedComponent<ComboEditor>? editor = null;
        editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, current).Add(component => component.CategoryBases, new[] { a, b, third })
            .Add(component => component.OnReplaceCombo, change => {
                current = change.NewCombo;
                editor!.SetParametersAndRender(next => next.Add(component => component.Combo, current));
            }));
        editor.Find(".requirement-edit").Click();
        Button(editor, "Add OR").Click();
        editor.Find("#comboTarget0").Change(third.Identity);
        editor.Find("form").Submit();
        Assert.That(editor.FindAll(".expanded-expression .expression-parenthesis"), Has.Count.EqualTo(2));
        Assert.That(editor.FindAll(".expanded-expression button button"), Is.Empty);
        editor.FindAll(".expanded-expression .expression-member").ToArray()[1].Click();
        Assert.That(editor.FindAll("form .requirement-property-picker"), Has.Count.EqualTo(1));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
    }

    [Test]
    public void ComboCompositeEditsOneConditionAndPreservesSiblings() {
        YGOProbabilityCalculatorBlazor.Models.CategoryBase third = new("C");
        YGOProbabilityCalculatorBlazor.Models.Combo current = new([
            YGOProbabilityCalculatorBlazor.Models.ComboCategory.SameCard([a, b], 1, 3)
        ]);
        IRenderedComponent<ComboEditor>? editor = null;
        editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, current).Add(component => component.CategoryBases, new[] { a, b, third })
            .Add(component => component.OnReplaceCombo, change => {
                current = change.NewCombo;
                editor!.SetParametersAndRender(next => next.Add(component => component.Combo, current));
            }));
        editor.FindAll(".expanded-expression .expression-member").ToArray()[1].Click();
        Assert.That(editor.FindAll("form .requirement-property-picker"), Has.Count.EqualTo(1));
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(b.Identity));
        editor.Find("#comboTarget0").Change(third.Identity);
        editor.FindAll(".expanded-expression .expression-member").ToArray()[0].Click();
        Assert.That(editor.Find("#comboTarget0").GetAttribute("value"), Is.EqualTo(a.Identity));
        Assert.That(current.Categories[0].CategoryBases, Is.EqualTo(new[] { a, b }));
        editor.Find("#comboTarget0").Change(third.Identity);
        editor.Find("form").Submit();
        Assert.That(current.Categories[0].CategoryBases, Is.EqualTo(new[] { third, b }));
        editor.FindAll(".expanded-expression .expression-member").ToArray()[1].Click();
        Button(editor, "Remove requirement").Click();
        Assert.That(current.Categories[0].CategoryBases, Is.EqualTo(new[] { third }));
        Assert.That((current.Categories[0].MinCount, current.Categories[0].MaxCount), Is.EqualTo((1, 3)));
    }

    [Test]
    public void SeparateComboRequirementsOfferSupportedConversionsAndOneInlineSubmit() {
        YGOProbabilityCalculatorBlazor.Models.Combo combo = new([new(a, 1, 3), new(b, 1, 3)]);
        IRenderedComponent<ComboEditor> editor = context.RenderComponent<ComboEditor>(parameters => parameters
            .Add(component => component.Combo, combo).Add(component => component.CategoryBases, new[] { a, b }));
        Assert.That(editor.Find(".expanded-expression > .expression-separator select").GetAttribute("value"), Is.EqualTo("+"));
        Assert.That(editor.FindAll(".expanded-expression > .expression-separator select option").Select(option => option.TextContent), Is.EqualTo(new[] { "+", "OR" }));
        editor.Find(".requirement-edit").Click();
        Assert.That(editor.FindAll("form button[type=submit]"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-edit-grid > .requirement-submit"), Has.Count.EqualTo(1));
        Assert.That(editor.FindAll(".requirement-bounds"), Has.Count.EqualTo(1));
    }

    [Test]
    public void SharedFormUsesOneSubmissionPathAndOwnsEscapeCancellation() {
        int submissions = 0;
        int cancellations = 0;
        IRenderedComponent<RequirementEditForm> editor = context.RenderComponent<RequirementEditForm>(parameters => parameters
            .Add(component => component.OnSubmit, () => submissions++)
            .Add(component => component.OnCancel, () => cancellations++)
            .Add(component => component.OperandContent, _ => builder => {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "type", "number");
                builder.CloseElement();
            }));
        editor.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.That(submissions, Is.Zero, "Enter must leave submission to the browser's native form action.");
        editor.Find("form").Submit();
        Assert.That(submissions, Is.EqualTo(1));
        editor.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(cancellations, Is.EqualTo(1));
    }
}
