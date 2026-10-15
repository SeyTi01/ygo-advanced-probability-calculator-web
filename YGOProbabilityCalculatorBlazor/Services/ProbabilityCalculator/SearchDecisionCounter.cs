using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A reduced ordered multi-valued decision diagram over original entry counts.
// Edges mean a count, not a choice of search assignment. Boolean OR therefore
// merges successful assignments before any physical-hand weights are counted.
// Each bound is a finite count automaton. Apply is Shannon expansion on the
// earliest variable, so AND/OR preserve exactly the original event predicates.
// Interning identical successor vectors and skipping a variable whose children
// agree are Boolean identities, including for maxima and overlapping searches.
// Count then partitions disjoint entry-count choices with C(copies, count).
// Skipped variables use Vandermonde's identity, C(sum copies, taken). The final
// cardinality restriction makes truncating each domain at handSize exact.
// All tables belong to one final-hand scenario and obey the request budget.
internal sealed class SearchDecisionCounter {
    private readonly WorkBudget _budget;
    private readonly int _handSize;
    private readonly int[] _copies;
    private readonly int[] _suffix;
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<Node, int> _unique = new();
    private readonly Dictionary<(bool Or, int Left, int Right), int> _apply = new();
    private readonly Dictionary<CountBound, int> _bounds = new();
    private readonly Dictionary<(int Node, int Level, int Left), BigInteger> _counts = new();
    private readonly Dictionary<(int N, int K), BigInteger> _binomials = new();
    private long _nodeCells;
    private long _countCells;
    private long _binomialCells;

    internal SearchDecisionCounter(List<Card> rows, int handSize, WorkBudget budget) {
        _budget = budget;
        _handSize = handSize;
        budget.CheckStorage(rows.Count + 1L, 2L * rows.Count + 1);
        _copies = rows.Select(card => card.Copies).ToArray();
        _suffix = new int[rows.Count + 1];
        for (int row = rows.Count - 1; row >= 0; row--) {
            _suffix[row] = checked(_suffix[row + 1] + _copies[row]);
        }
        _nodes.Add(new(rows.Count, []));
        _nodes.Add(new(rows.Count, []));
    }

    internal int Union(IEnumerable<CompiledEvent?> events) {
        int result = 0;
        foreach (CompiledEvent predicate in events.OfType<CompiledEvent>()) {
            int conjunction = 1;
            foreach (CountBound bound in predicate.Constraints) {
                conjunction = Apply(false, conjunction, Bound(bound));
                if (conjunction == 0) {
                    break;
                }
            }
            result = Apply(true, result, conjunction);
        }
        return result;
    }

    internal int Or(int first, int second) => Apply(true, first, second);

    internal BigInteger Count(int root) => Count(root, 0, _handSize);

    private int Bound(CountBound bound) {
        if (_bounds.TryGetValue(bound, out int cached)) {
            return cached;
        }
        int[] eligible = new int[_copies.Length + 1];
        for (int row = _copies.Length - 1; row >= 0; row--) {
            _budget.Spend(WorkBudget.IntegerCells(bound.EligibleRows) + 1);
            eligible[row] = eligible[row + 1] + (((bound.EligibleRows >> row) & 1) != 0 ? _copies[row] : 0);
        }
        Dictionary<(int Row, int Min, int Max), int> memo = new();
        // A maximum of handSize is tautological on the counted sample space.
        // Dropping it avoids constructing irrelevant overfull-hand branches.
        int result = Visit(0, bound.MinCount, bound.MaxCount == _handSize ? eligible[0] : bound.MaxCount);
        _budget.CheckStorage(_bounds.Count + 1, (_bounds.Count + 1L) * (4 + _copies.Length / 32));
        _bounds.Add(bound, result);
        _budget.SearchDiagnostics?.Add("unique bounds");
        return result;

        int Visit(int row, int min, int max) {
            _budget.Spend(1);
            min = Math.Max(0, min);
            max = Math.Min(eligible[row], max);
            if (min > max) {
                return 0;
            }
            if (min == 0 && max == eligible[row]) {
                return 1;
            }
            if (((bound.EligibleRows >> row) & 1) == 0) {
                return Visit(row + 1, min, max);
            }
            (int, int, int) key = (row, min, max);
            if (memo.TryGetValue(key, out int previous)) {
                return previous;
            }
            _budget.CheckStorage(Math.Min(_copies[row], _handSize) + 1L, Math.Min(_copies[row], _handSize) + 1L);
            int[] children = new int[Math.Min(_copies[row], _handSize) + 1];
            for (int count = 0; count < children.Length; count++) {
                children[count] = Visit(row + 1, min - count, max - count);
            }
            int node = Intern(row, children);
            _budget.CheckStorage(memo.Count + 1, (memo.Count + 1L) * 4);
            memo.Add(key, node);
            return node;
        }
    }

    private int Apply(bool or, int left, int right) {
        _budget.Spend(1);
        if (left == right) {
            return left;
        }
        if (left > right) {
            (left, right) = (right, left);
        }
        if (left == 0) {
            return or ? right : 0;
        }
        if (left == 1) {
            return or ? 1 : right;
        }
        (bool, int, int) key = (or, left, right);
        if (_apply.TryGetValue(key, out int cached)) {
            _budget.SearchDiagnostics?.Add("apply cache hits");
            return cached;
        }
        Node first = _nodes[left];
        Node second = _nodes[right];
        int level = Math.Min(first.Level, second.Level);
        _budget.CheckStorage(Math.Min(_copies[level], _handSize) + 1L, Math.Min(_copies[level], _handSize) + 1L);
        int[] children = new int[Math.Min(_copies[level], _handSize) + 1];
        for (int count = 0; count < children.Length; count++) {
            children[count] = Apply(
                or,
                first.Level == level ? first.Children[count] : left,
                second.Level == level ? second.Children[count] : right
            );
        }
        int result = Intern(level, children);
        _budget.CheckStorage(_apply.Count + 1, (_apply.Count + 1L) * 4);
        _apply.Add(key, result);
        _budget.SearchDiagnostics?.Add("apply cache entries");
        return result;
    }

    private int Intern(int level, int[] children) {
        _budget.Spend(children.Length + 1L);
        bool identical = true;
        foreach (int child in children) {
            identical &= child == children[0];
        }
        if (identical) {
            return children[0];
        }
        Node node = new(level, children);
        if (_unique.TryGetValue(node, out int existing)) {
            return existing;
        }
        _nodeCells += children.Length + 2L;
        _budget.CheckStorage(_nodes.Count + 1, _nodeCells);
        int index = _nodes.Count;
        _budget.SearchDiagnostics?.Add("decision nodes");
        _nodes.Add(node);
        _unique.Add(node, index);
        return index;
    }

    private BigInteger Count(int node, int level, int left) {
        _budget.Spend(1);
        if (node == 0 || left < 0 || left > _suffix[level]) {
            return 0;
        }
        if (node == 1) {
            return Choose(_suffix[level], left);
        }
        (int, int, int) key = (node, level, left);
        if (_counts.TryGetValue(key, out BigInteger cached)) {
            _budget.SearchDiagnostics?.Add("count cache hits");
            return cached;
        }
        _budget.SearchDiagnostics?.Add("weighted count states");
        Node current = _nodes[node];
        BigInteger result = 0;
        if (level < current.Level) {
            int skipped = _suffix[level] - _suffix[current.Level];
            for (int take = Math.Max(0, left - _suffix[current.Level]); take <= Math.Min(left, skipped); take++) {
                Add(Choose(skipped, take), Count(node, current.Level, left - take));
            }
        }
        else {
            for (int take = Math.Max(0, left - _suffix[level + 1]); take <= Math.Min(left, _copies[level]); take++) {
                Add(Choose(_copies[level], take), Count(current.Children[take], level + 1, left - take));
            }
        }
        // A saturated memo stops retaining results, never changes the count.
        if (_counts.Count < 2048) {
            _countCells += WorkBudget.IntegerCells(result) + 3;
            _budget.CheckStorage(_counts.Count + 1, _countCells);
            _counts.Add(key, result);
        }
        return result;

        void Add(BigInteger weight, BigInteger count) {
            _budget.Spend((WorkBudget.IntegerCells(weight) + 1) * (WorkBudget.IntegerCells(count) + 1)
                + WorkBudget.IntegerCells(result) + 1);
            result += weight * count;
            _budget.CheckStorage(1, WorkBudget.IntegerCells(result));
        }
    }

    private BigInteger Choose(int n, int k) {
        k = Math.Min(k, n - k);
        if (k < 0) {
            return 0;
        }
        if (_binomials.TryGetValue((n, k), out BigInteger cached)) {
            return cached;
        }
        BigInteger result = 1;
        for (int i = 1; i <= k; i++) {
            _budget.Spend(WorkBudget.IntegerCells(result) * 2 + 1);
            result = result * (n - i + 1) / i;
            _budget.CheckStorage(1, WorkBudget.IntegerCells(result));
        }
        _binomialCells += WorkBudget.IntegerCells(result) + 2;
        _budget.CheckStorage(_binomials.Count + 1, _binomialCells);
        _binomials.Add((n, k), result);
        return result;
    }

    private sealed record Node(int Level, int[] Children) {
        private readonly int _hash = Hash(Level, Children);
        public bool Equals(Node? other) => other is not null && Level == other.Level && Children.AsSpan().SequenceEqual(other.Children);
        public override int GetHashCode() => _hash;
        private static int Hash(int level, int[] children) {
            HashCode hash = new();
            hash.Add(level);
            foreach (int child in children) {
                hash.Add(child);
            }
            return hash.ToHashCode();
        }
    }
}
