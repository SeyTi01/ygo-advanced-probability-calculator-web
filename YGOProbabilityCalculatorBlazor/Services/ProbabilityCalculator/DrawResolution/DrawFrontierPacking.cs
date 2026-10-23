namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A bijection on bounded vectors, not a new state equivalence. Preserve every
// count, flag, phase and FIFO entry, including trailing zero-valued candidates.
// Each map uses just one encoding. Search terminal keys can use the same bijection.
internal static class DrawFrontierPacking {
    internal static int[] Pack(int[] values) {
        int[] packed = new int[1 + (values.Length + 3) / 4];
        packed[0] = values.Length;
        for (int i = 0; i < values.Length; i++) {
            packed[1 + i / 4] |= checked((byte)values[i]) << (8 * (i % 4));
        }
        return packed;
    }

    internal static int[] Unpack(int[] packed) {
        int[] values = new int[packed[0]];
        for (int i = 0; i < values.Length; i++) {
            values[i] = (packed[1 + i / 4] >> (8 * (i % 4))) & byte.MaxValue;
        }
        return values;
    }
}
