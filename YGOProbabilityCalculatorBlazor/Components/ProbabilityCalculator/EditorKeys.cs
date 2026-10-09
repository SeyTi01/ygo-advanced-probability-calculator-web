namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Model references identify rows on load/reorder; edits explicitly transfer their UI identity.
internal sealed class EditorKeys<T> where T : class {
    private readonly Dictionary<T, object> _keys = new(ReferenceEqualityComparer.Instance);

    public object this[T item] => _keys[item];

    public void Synchronize(IReadOnlyList<T> items) {
        HashSet<T> current = new(items, ReferenceEqualityComparer.Instance);

        foreach (T removed in _keys.Keys.Where(item => !current.Contains(item)).ToList()) {
            _keys.Remove(removed);
        }

        foreach (T item in items) {
            _keys.TryAdd(item, new object());
        }
    }

    public void Replace(T oldItem, T newItem) {
        object key = _keys[oldItem];
        _keys.Remove(oldItem);
        _keys[newItem] = key;
    }
}
