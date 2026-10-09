namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal static class ListMove {
    public static bool TryMove<T>(List<T> items, int from, int to) {
        if (from < 0 || from >= items.Count || to < 0 || to >= items.Count || from == to) {
            return false;
        }

        T item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);

        return true;
    }

    public static int AdjustActiveIndex(int active, int from, int to) {
        if (active == from) {
            return to;
        }

        if (from < active && active <= to) {
            return active - 1;
        }

        if (to <= active && active < from) {
            return active + 1;
        }

        return active;
    }
}
