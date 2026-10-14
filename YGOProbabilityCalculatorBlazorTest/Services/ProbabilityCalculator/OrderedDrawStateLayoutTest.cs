using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class OrderedDrawStateLayoutTest {
    [TestCase(false, false, 12)]
    [TestCase(true, false, 13)]
    [TestCase(false, true, 13)]
    [TestCase(true, true, 14)]
    public void OptionalRegionsAndIndependentOptEntriesKeepOriginalEncoding(bool hidden, bool timing, int queueStart) {
        // Same ID still represents distinct entries. Ordinary and non-OPT rows
        // have no OPT slot; a zero-copy effect retains its descriptor slot.
        List<Card> cards = [new([], id: "ordinary"),
            new([], id: "shared", drawCount: 2, preDrawDeckRemovalCount: hidden ? 3 : 0),
            new([], id: "non-opt", drawCount: 1, drawOncePerTurn: false,
                firstActivationOnly: timing, blocksLaterDraws: timing),
            new([], 0, id: "shared", drawCount: 2, postDrawDeckMillCount: hidden ? 2 : 0)];
        OrderedDrawStateLayout layout = new(cards);
        Assert.Multiple(() => {
            Assert.That(layout.RowCount, Is.EqualTo(4));
            Assert.That(layout.HandStart, Is.EqualTo(4));
            Assert.That(Enumerable.Range(0, 4).Select(layout.OptSlot), Is.EqualTo(new[] { -1, 8, -1, 9 }));
            Assert.That(layout.DrawsLeft, Is.EqualTo(10));
            Assert.That(layout.CompletionSource, Is.EqualTo(11));
            Assert.That(layout.HasHiddenCount, Is.EqualTo(hidden));
            Assert.That(layout.HiddenCount, Is.EqualTo(hidden ? 12 : -1));
            Assert.That(layout.HasActivationState, Is.EqualTo(timing));
            Assert.That(layout.ActivationState, Is.EqualTo(timing ? (hidden ? 13 : 12) : -1));
            Assert.That(layout.QueueStart, Is.EqualTo(queueStart));
        });

        // Exercise the actual wire-free packed representation with every region
        // populated, a repeated non-OPT candidate and a trailing row-zero entry.
        int[] values = new int[queueStart + 4];
        new[] { 2, 1, 3, 0, 1, 2, 2, 0, 1, 0, 2, 3 }.CopyTo(values, 0);
        if (hidden) {
            values[layout.HiddenCount] = 5;
        }
        if (timing) {
            values[layout.ActivationState] = 2;
        }
        new[] { 1, 2, 2, 0 }.CopyTo(values, queueStart);
        int[] decoded = DrawFrontierPacking.Unpack(DrawFrontierPacking.Pack(values));
        Assert.Multiple(() => {
            Assert.That(decoded, Is.EqualTo(values));
            Assert.That(decoded.AsSpan(layout.HandStart, layout.RowCount).ToArray(), Is.EqualTo(new[] { 1, 2, 2, 0 }));
            Assert.That(decoded[layout.OptSlot(1)], Is.EqualTo(1));
            Assert.That(decoded[layout.OptSlot(3)], Is.Zero);
            Assert.That(decoded[layout.CompletionSource], Is.EqualTo(3));
            Assert.That(decoded.AsSpan(layout.QueueStart).ToArray(), Is.EqualTo(new[] { 1, 2, 2, 0 }));
        });
    }

    [Test]
    public void EmptyDeckRetainsOpeningBatchAndCompletionCells() {
        OrderedDrawStateLayout layout = new([]);
        Assert.Multiple(() => {
            Assert.That(layout.RowCount, Is.Zero);
            Assert.That(layout.DrawsLeft, Is.Zero);
            Assert.That(layout.CompletionSource, Is.EqualTo(1));
            Assert.That(layout.QueueStart, Is.EqualTo(2));
            Assert.That(layout.HasHiddenCount || layout.HasActivationState, Is.False);
        });
    }
}
