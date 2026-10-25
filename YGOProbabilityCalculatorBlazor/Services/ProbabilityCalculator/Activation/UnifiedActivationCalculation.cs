using System.Numerics;
using System.Text;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Activation;

// Causal decision/chance recursion. Each result row has its own objective: search
// alternatives maximize that row BEFORE future observations; chance outcomes sum.
// Search inspection observes the current unordered Main Deck, never its order.
internal sealed partial class UnifiedActivationCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    DrawActivationPriority? priority = null
) {
    private List<Card> _cards = [];
    private List<Combo> _combos = [];
    private int[][] _objectives = [];
    private bool[] _relevant = [];
    private bool[][] _costEligible = [];
    private bool[][] _removalEligible = [];
    private SearchMode[] _modes = [];
    private int[][] _sourceModes = [];
    private readonly Dictionary<string, ExactProbability[]> _memo = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HandBound[][][]> _events = [];
    private long _memoCells;
    private long _eventCells;
    private long _activeCells;
    private int _activeStates;
    private int _activePayloads;

    private sealed record SearchMode(int Source, int Limit, int[] Targets);
    private sealed record HandBound(int[] Rows, int Minimum, int Maximum);

    private sealed class State(int[] pool, int rowCount, int modeCount, int drawsLeft) {
        internal int[] Pool = pool;
        internal int[] Hand = new int[rowCount];
        // Eligible distinguishes naturally drawn copies from fetched/declined copies.
        internal int[] Eligible = new int[rowCount];
        internal int[] Used = new int[rowCount];
        internal int[] ModeUses = new int[modeCount];
        internal List<int> Queue = [];
        internal int DrawsLeft = drawsLeft;
        internal int CompletionSource = -1;
        internal int Hidden;
        // Only DRAW activations change first-draw/locked state.
        internal int DrawState;
        internal int Capacity => Pool.Sum() - Hidden;
        internal long Cells => 4L * Pool.Length + ModeUses.Length + Queue.Count + 5;

        internal State Copy() {
            State copy = (State)MemberwiseClone();
            copy.Pool = (int[])Pool.Clone();
            copy.Hand = (int[])Hand.Clone();
            copy.Eligible = (int[])Eligible.Clone();
            copy.Used = (int[])Used.Clone();
            copy.ModeUses = (int[])ModeUses.Clone();
            copy.Queue = [.. Queue];
            return copy;
        }
    }

    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false) {
        if (deck.Any(card => card.Copies < 0)) {
            throw new ArgumentOutOfRangeException(nameof(deck));
        }
        _memo.Clear();
        _events.Clear();
        _memoCells = 0;
        _eventCells = 0;
        _activeCells = 0;
        _activeStates = 0;
        _activePayloads = 0;
        _combos = combos;
        _cards = CompileCards(deck.Where(card => card.Active && card.Copies > 0).ToList(), combos);
        int copies = _cards.Sum(card => card.Copies);
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }
        List<int[]> objectives = [Enumerable.Range(0, combos.Count).ToArray()];
        if (!totalOnly) {
            objectives.AddRange(Enumerable.Range(0, combos.Count).Select(index => new[] { index }));
            objectives.AddRange((groups ?? []).Select(group => Enumerable.Range(0, combos.Count)
                .Where(index => combos[index].GroupId == group.Id).ToArray()));
        }
        _objectives = objectives.ToArray();
        budget.CheckStorage(_objectives.Length, _objectives.Sum(objective => objective.Length + 1L));
        CompilePolicy();
        State initial = new(_cards.Select(card => card.Copies).ToArray(), _cards.Count, _modes.Length, handSize);
        ExactProbability[] result = Solve(initial);
        return new(result[0], totalOnly ? [] : result.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : result.Skip(1 + combos.Count).ToArray());
    }

    private ExactProbability[] Solve(State state) {
        budget.Spend(state.Cells + 1);
        long retainedCells = state.Cells;
        _activeStates++;
        _activeCells += retainedCells;
        CheckActiveStorage();
        try {
            if (state.DrawsLeft == 0 && !Complete(state)) {
                return Zero();
            }
            string key = Key(state);
            using PayloadLease keyStorage = Retain((key.Length + 1L) / 2);
            if (_memo.TryGetValue(key, out ExactProbability[]? cached)) {
                return cached;
            }
            ExactProbability[] result;
            if (state.DrawsLeft > 0) {
                result = Draw(state);
            }
            else {
                int candidate = SelectCandidate(state);
                if (candidate < 0) {
                    result = Terminal(state.Hand);
                }
                else {
                    int source = state.Queue[candidate];
                    result = _cards[source].Search is not null
                        ? state.Hidden > 0 ? InspectDeck(state) : Search(state, candidate)
                        : ActivateDraw(state, candidate);
                }
            }
            Remember(key, result);
            return result;
        }
        finally {
            _activeCells -= retainedCells;
            _activeStates--;
        }
    }

    private ExactProbability[] Draw(State state) {
        if (state.DrawsLeft > state.Capacity) {
            throw new InvalidOperationException("An activated batch exceeds the physical remaining deck.");
        }
        ExactProbability[] result = Zero();
        using PayloadLease resultStorage = Retain(ResultCells(result));
        int unseen = state.Pool.Sum();
        for (int row = 0; row < _cards.Count; row++) {
            budget.Spend(1);
            if (state.Pool[row] == 0) {
                continue;
            }
            State child = state.Copy();
            child.Pool[row]--;
            child.Hand[row]++;
            child.DrawsLeft--;
            if (_cards[row].HasEffect && !Exhausted(child, row) && TimingAllows(child, row)) {
                child.Eligible[row]++;
                child.Queue.Add(row);
            }
            AddWeighted(result, Solve(child), new(state.Pool[row], unseen));
            resultStorage.Resize(ResultCells(result));
        }
        return result;
    }

    // Hidden losses are a uniform subset of the still-unobserved pool. A visible
    // draw is uniform in that pool, while capacity excludes Hidden. At inspection
    // the joint survivor multiset is hypergeometric; average these observations
    // before making any target/mode/decline decision within each observed state.
    private ExactProbability[] InspectDeck(State state) {
        ExactProbability[] result = Zero();
        using PayloadLease resultStorage = Retain(ResultCells(result));
        int rows = _cards.Count;
        int capacity = state.Capacity;
        int[] survivors = new int[rows];
        int[] suffix = new int[rows + 1];
        int[] left = new int[rows + 1];
        int[] next = new int[rows];
        int[] maximum = new int[rows];
        BigInteger[] weights = new BigInteger[rows + 1];
        for (int row = rows - 1; row >= 0; row--) {
            suffix[row] = suffix[row + 1] + state.Pool[row];
        }
        BigInteger denominator = Choose(suffix[0], capacity);
        using PayloadLease inspectionStorage = Retain(5L * rows + 2 + WorkBudget.IntegerCells(denominator));
        weights[0] = BigInteger.One;
        left[0] = capacity;
        int depth = 0;
        Initialize(0);
        while (depth >= 0) {
            budget.Spend(rows + WorkBudget.IntegerCells(weights[depth]) + 1L);
            if (depth == rows) {
                State observed = state.Copy();
                observed.Pool = (int[])survivors.Clone();
                observed.Hidden = 0;
                AddWeighted(result, Solve(observed), new(weights[depth], denominator));
                resultStorage.Resize(ResultCells(result));
                depth--;
                continue;
            }
            if (next[depth] > maximum[depth]) {
                depth--;
                continue;
            }
            int count = next[depth]++;
            survivors[depth] = count;
            weights[depth + 1] = weights[depth] * Choose(state.Pool[depth], count);
            left[depth + 1] = left[depth] - count;
            inspectionStorage.Resize(5L * rows + 2 + WorkBudget.IntegerCells(denominator)
                + weights.Sum(WorkBudget.IntegerCells));
            depth++;
            if (depth < rows) {
                Initialize(depth);
            }
        }
        return result;

        void Initialize(int row) {
            next[row] = Math.Max(0, left[row] - suffix[row + 1]);
            maximum[row] = Math.Min(left[row], state.Pool[row]);
        }
    }

    private ExactProbability[] Search(State state, int candidate) {
        int source = state.Queue[candidate];
        // Decline permanently retires this physical candidate; its card remains.
        State declined = state.Copy();
        Retire(declined, candidate);
        ExactProbability[] best = (ExactProbability[])Solve(declined).Clone();
        using PayloadLease bestStorage = Retain(ResultCells(best));
        if (best.All(value => value.Numerator == value.Denominator)) {
            return best;
        }
        foreach (int modeIndex in _sourceModes[source]) {
            SearchMode mode = _modes[modeIndex];
            if (state.ModeUses[modeIndex] >= mode.Limit) {
                continue;
            }
            foreach (int target in mode.Targets) {
                budget.Spend(1);
                if (state.Pool[target] == 0) {
                    continue;
                }
                State activated = state.Copy();
                activated.Queue.RemoveAt(candidate);
                // Preserve the established search payment timing: consume the
                // source before fixed hand-cost choices, then retrieve the target.
                activated.Hand[source]--;
                activated.Eligible[source]--;
                activated.Used[source]++;
                activated.ModeUses[modeIndex]++;
                if (!Remove(activated, _cards[source].PreDrawRemovalCount, _costEligible[source])) {
                    throw new InvalidOperationException("A checked search cost became unavailable.");
                }
                activated.Pool[target]--;
                activated.Hand[target]++;
                // Fetched effects deliberately never enter Eligible or Queue.
                if (Remove(activated, _cards[source].PostDrawDiscardCount, _removalEligible[source])) {
                    Maximize(best, Solve(activated));
                    bestStorage.Resize(ResultCells(best));
                }
            }
        }
        return best;
    }

    private ExactProbability[] ActivateDraw(State state, int candidate) {
        int source = state.Queue[candidate];
        Card card = _cards[source];
        State activated = state.Copy();
        activated.Queue.RemoveAt(candidate);
        if (!Remove(activated, card.PreDrawRemovalCount, _costEligible[source], source)) {
            throw new InvalidOperationException("A checked draw cost became unavailable.");
        }
        activated.Hand[source]--;
        activated.Eligible[source]--;
        activated.Used[source]++;
        activated.Hidden += card.PreDrawDeckRemovalCount;
        activated.DrawState = 1;
        activated.DrawsLeft = card.DrawCount!.Value;
        activated.CompletionSource = source;
        return Solve(activated);
    }

    private bool Complete(State state) {
        int source = state.CompletionSource;
        if (source < 0) {
            return true;
        }
        Card card = _cards[source];
        state.Hidden += card.PostDrawDeckMillCount;
        if (card.BlocksLaterDraws) {
            state.DrawState = 2;
        }
        state.CompletionSource = -1;
        return Remove(state, card.PostDrawDiscardCount, _removalEligible[source]);
    }

    private int SelectCandidate(State state) {
        int selected = -1;
        int rank = int.MaxValue;
        int size = state.Hand.Sum();
        int capacity = state.Capacity;
        for (int candidate = 0; candidate < state.Queue.Count;) {
            budget.Spend(_cards.Count + 1L);
            int source = state.Queue[candidate];
            Card card = _cards[source];
            bool unavailable = Exhausted(state, source) || !TimingAllows(state, source)
                || DeckConsumption(source) > capacity;
            if (unavailable) {
                Retire(state, candidate);
                continue;
            }
            // Missing physical targets block activation but do not erase the
            // naturally held source from the fixed, plausibility-based keep policy.
            if ((card.Search is not null && !HasPossibleTarget(state, source))
                || size < card.MinimumHandSize || !CanPay(state, source)) {
                candidate++;
                continue;
            }
            int nextRank = priority?.Rank(card.Id) ?? int.MaxValue;
            if (selected < 0 || nextRank < rank) {
                selected = candidate;
                rank = nextRank;
            }
            candidate++;
        }
        return selected;
    }

    private bool HasPossibleTarget(State state, int source) {
        foreach (int modeIndex in _sourceModes[source]) {
            SearchMode mode = _modes[modeIndex];
            if (state.ModeUses[modeIndex] < mode.Limit && mode.Targets.Any(target => state.Pool[target] > 0)) {
                return true;
            }
        }
        return false;
    }

    private bool Exhausted(State state, int source) => state.Used[source] >= _cards[source].MaximumActivations;
    private bool TimingAllows(State state, int source) => _cards[source].DrawCount is null
        || (state.DrawState != 2 && (!_cards[source].FirstActivationOnly || state.DrawState == 0));
    private int DeckConsumption(int source) => _cards[source].Search is not null ? 1
        : _cards[source].DrawCount!.Value + _cards[source].PreDrawDeckRemovalCount + _cards[source].PostDrawDeckMillCount;

    private bool CanPay(State state, int source, int reservedSource = -1) {
        int available = 0;
        for (int row = 0; row < _cards.Count; row++) {
            budget.Spend(1);
            if (_costEligible[source][row]) {
                available += state.Hand[row] - (row == source ? 1 : 0) - (row == reservedSource ? 1 : 0);
            }
        }
        return available >= _cards[source].PreDrawRemovalCount;
    }

    private static void Retire(State state, int candidate) {
        state.Eligible[state.Queue[candidate]]--;
        state.Queue.RemoveAt(candidate);
    }

    // Keep the established deterministic policy: positive requirements first,
    // then usable draws/searches, then draw sources whose costs may become payable.
    // Plausible search targets use original copies minus held counts; inspection
    // never gives this fixed discard policy extra knowledge of hidden identities.
    private bool Remove(State state, int count, bool[] eligibility, int reservedSource = -1) {
        for (int removal = 0; removal < count; removal++) {
            int capacity = state.Capacity;
            int size = state.Hand.Sum();
            bool[] pending = new bool[_cards.Count];
            foreach (int source in state.Queue) {
                pending[source] = true;
            }
            bool[] usable = new bool[_cards.Count];
            bool[] deferred = new bool[_cards.Count];
            int minimumDrawConsumption = int.MaxValue;
            if (reservedSource >= 0 && _cards[reservedSource].DrawCount is not null) {
                minimumDrawConsumption = DeckConsumption(reservedSource);
            }
            for (int row = 0; row < _cards.Count; row++) {
                Card card = _cards[row];
                int allowance = card.MaximumActivations - state.Used[row] - (row == reservedSource ? 1 : 0);
                if (card.DrawCount is null || !pending[row] || allowance <= 0 || !TimingAllows(state, row)
                    || (reservedSource >= 0 && _cards[reservedSource].DrawCount is not null
                        && (_cards[reservedSource].BlocksLaterDraws || card.FirstActivationOnly))
                    || DeckConsumption(row) > capacity) {
                    continue;
                }
                bool canPay = size - (reservedSource < 0 ? 0 : 1) >= card.MinimumHandSize && CanPay(state, row, reservedSource);
                usable[row] = canPay;
                deferred[row] = !canPay && (card.PreDrawRemovalCount > 0 || card.MinimumHandSize > 0);
                if (canPay) {
                    minimumDrawConsumption = Math.Min(minimumDrawConsumption, DeckConsumption(row));
                }
            }
            int selected = -1;
            int worst = 0;
            for (int row = 0; row < _cards.Count; row++) {
                budget.Spend(_cards.Count + 1L);
                int held = state.Hand[row] - (row == reservedSource ? 1 : 0);
                if (held <= 0 || !eligibility[row]) {
                    continue;
                }
                Card card = _cards[row];
                int allowance = card.MaximumActivations - state.Used[row] - (row == reservedSource ? 1 : 0);
                bool redundant = held > allowance;
                bool canDefer = deferred[row] && !redundant && !card.FirstActivationOnly
                    && minimumDrawConsumption != int.MaxValue && DeckConsumption(row) <= capacity - minimumDrawConsumption;
                int tier = _relevant[row] ? 1 : usable[row] && !redundant ? 2 : canDefer ? 3 : 4;
                int searchCapacity = capacity - (reservedSource < 0 ? 0 : DeckConsumption(reservedSource));
                if (card.Search is not null && pending[row] && allowance > 0 && searchCapacity > 0) {
                    foreach (int modeIndex in _sourceModes[row]) {
                        SearchMode mode = _modes[modeIndex];
                        if (state.ModeUses[modeIndex] >= mode.Limit) {
                            continue;
                        }
                        foreach (int target in mode.Targets) {
                            if (_cards[target].Copies > state.Hand[target]) {
                                tier = Math.Min(tier, _relevant[target] ? 1 : 2);
                            }
                        }
                    }
                }
                if (tier >= worst) {
                    selected = row;
                    worst = tier;
                }
            }
            if (selected < 0) {
                return false;
            }
            state.Hand[selected]--;
            // Discard an inert fetched/declined copy first. If an eligible copy
            // must leave, remove its latest FIFO occurrence.
            if (state.Eligible[selected] > state.Hand[selected]) {
                int queued = state.Queue.LastIndexOf(selected);
                if (queued >= 0) {
                    Retire(state, queued);
                }
                else {
                    throw new InvalidOperationException("A reserved source was selected as a hand payment.");
                }
            }
        }
        return true;
    }

    private ExactProbability[] Terminal(int[] hand) {
        HandBound[][][] events = Events(hand.Sum());
        bool[] matches = new bool[_combos.Count];
        for (int combo = 0; combo < matches.Length; combo++) {
            matches[combo] = events[combo].Any(route => route.All(bound => {
                budget.Spend(bound.Rows.Length + 1L);
                int count = bound.Rows.Sum(row => hand[row]);
                return count >= bound.Minimum && count <= bound.Maximum;
            }));
        }
        return _objectives.Select(objective => objective.Any(combo => matches[combo]) ? ExactProbability.One : ExactProbability.Zero).ToArray();
    }

    private HandBound[][][] Events(int size) {
        if (_events.TryGetValue(size, out HandBound[][][]? cached)) {
            return cached;
        }
        ComboEventCompiler compiler = new(_cards, size, budget);
        HandBound[][][] events = _combos.Select(combo => compiler.CompileCombo(combo)
            .Where(route => route is not null).Select(route => route!.Constraints.Select(bound => {
                int[] rows = Enumerable.Range(0, _cards.Count).Where(row => (bound.EligibleRows & (BigInteger.One << row)) != 0).ToArray();
                _eventCells += rows.Length + 3;
                return new HandBound(rows, bound.MinCount, bound.MaxCount);
            }).ToArray()).ToArray()).ToArray();
        _eventCells += events.Sum(routes => routes.Length + 1L);
        budget.CheckStorage(_events.Count + 1, _eventCells);
        _events.Add(size, events);
        return events;
    }

    private ExactProbability[] Zero() => Enumerable.Repeat(ExactProbability.Zero, _objectives.Length).ToArray();

    private void AddWeighted(ExactProbability[] result, ExactProbability[] values, ExactProbability weight) {
        for (int row = 0; row < result.Length; row++) {
            result[row] = result[row].Add(values[row].Multiply(weight, budget), budget);
        }
    }

    private void Maximize(ExactProbability[] result, ExactProbability[] values) {
        for (int row = 0; row < result.Length; row++) {
            long cells = result[row].Cells + values[row].Cells + 1;
            budget.Spend(cells * cells);
            if (values[row].Numerator * result[row].Denominator > result[row].Numerator * values[row].Denominator) {
                result[row] = values[row];
            }
        }
    }

    private BigInteger Choose(int total, int chosen) {
        chosen = Math.Min(chosen, total - chosen);
        BigInteger result = BigInteger.One;
        for (int index = 1; index <= chosen; index++) {
            budget.Spend(WorkBudget.IntegerCells(result) + 1);
            result = result * (total - chosen + index) / index;
            budget.CheckStorage(1, WorkBudget.IntegerCells(result) + 1);
        }
        return result;
    }

    private string Key(State state) {
        StringBuilder key = new();
        Append(state.Pool);
        Append(state.Hand);
        Append(state.Eligible);
        Append(state.Used);
        Append(state.ModeUses);
        Append(state.Queue);
        key.Append(state.DrawsLeft).Append(',').Append(state.CompletionSource).Append(',').Append(state.Hidden).Append(',').Append(state.DrawState);
        budget.Spend(key.Length + 1L);
        return key.ToString();

        void Append(IEnumerable<int> values) {
            foreach (int value in values) {
                key.Append(value).Append(',');
            }
            key.Append(';');
        }
    }

    private static long ResultCells(ExactProbability[] result) => result.Length + result.Sum(value => value.Cells);

    private PayloadLease Retain(long cells) => new(this, cells);

    // Track payload retained across recursive calls separately from memo/event
    // maps. Cache eviction does not hide an oversized active decision stack.
    private sealed class PayloadLease : IDisposable {
        private readonly UnifiedActivationCalculation _owner;
        private long _cells;

        internal PayloadLease(UnifiedActivationCalculation owner, long cells) {
            _owner = owner;
            owner._activePayloads++;
            Resize(cells);
        }

        internal void Resize(long cells) {
            _owner._activeCells += cells - _cells;
            _cells = cells;
            _owner.CheckActiveStorage();
        }

        public void Dispose() {
            _owner._activeCells -= _cells;
            _owner._activePayloads--;
        }
    }

    private void CheckActiveStorage() => budget.CheckStorage(_activeStates + _activePayloads, _activeCells);

    private void Remember(string key, ExactProbability[] result) {
        long cells = (key.Length + 1L) / 2 + result.Sum(value => value.Cells) + result.Length;
        // Memoization is optional. Evicting it changes runtime, never probability;
        // exact calculation can continue within the selected Storage allowance.
        if (_memo.Count + 1 > budget.StoragePolicy.EntryLimit || _memoCells + cells > budget.StoragePolicy.CellLimit) {
            _memo.Clear();
            _memoCells = 0;
        }
        budget.CheckStorage(_memo.Count + 1L, _memoCells + cells);
        if (_memo.TryAdd(key, result)) {
            _memoCells += cells;
        }
    }
}
