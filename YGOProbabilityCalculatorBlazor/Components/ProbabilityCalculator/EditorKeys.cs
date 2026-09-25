namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Model references identify rows on load/reorder; edits explicitly transfer their UI identity.
internal sealed class EditorKeys<T> where T : class {
    private readonly Dictionary<T, object> keys = new(ReferenceEqualityComparer.Instance);

    public object this[T item] => keys[item];

    public void Synchronize(IReadOnlyList<T> items) {
        var current = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        foreach (var removed in keys.Keys.Where(item => !current.Contains(item)).ToList())
            keys.Remove(removed);
        foreach (var item in items)
            keys.TryAdd(item, new object());
    }

    public void Replace(T oldItem, T newItem) {
        var key = keys[oldItem];
        keys.Remove(oldItem);
        keys[newItem] = key;
    }
}
