using CardProbability.Internal;
using CardProbability.Internal.Counting;

namespace CardProbability.Internal.DrawResolution;

internal sealed record DrawResolution(
    Dictionary<DrawComposition, ExactProbability> Scenarios
);
