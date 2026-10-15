using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed partial class SearchDecisionCounter {
    // Shares one Boolean diagram and one joint weighted memo across total,
    // combos, and groups. Search assignments are never probabilistic branches.
    internal sealed class SurvivorCounter {
        private readonly SearchDecisionCounter _diagram;
        private readonly PhysicalSearchScenario _scenario;
        private readonly WorkBudget _budget;
        private readonly int[] _ordinarySuffix;
        private readonly int[] _unseenSuffix;
        private readonly bool[] _needsSurvivors;
        private readonly Dictionary<(int Node, int Row, int Hand, int Loss), BigInteger> _memo = new();
        private long _cells;

        internal SurvivorCounter(SearchDecisionCounter diagram, PhysicalSearchScenario scenario) {
            _diagram = diagram;
            _scenario = scenario;
            _budget = diagram._budget;
            _budget.CheckStorage(scenario.Cards.Count + 1L, 2L * (scenario.Cards.Count + 1));
            _budget.CheckStorage(diagram._nodes.Count, diagram._nodes.Count);
            _needsSurvivors = new bool[diagram._nodes.Count];
            for (int node = 2; node < diagram._nodes.Count; node++) {
                Node current = diagram._nodes[node];
                _budget.Spend(current.Children.Length + 1L);
                _needsSurvivors[node] = current.Level % 2 == 1 || current.Children.Any(child => _needsSurvivors[child]);
            }
            _ordinarySuffix = new int[scenario.Cards.Count + 1];
            _unseenSuffix = new int[scenario.Cards.Count + 1];
            for (int row = scenario.Cards.Count - 1; row >= 0; row--) {
                _budget.Spend(1);
                int copies = scenario.Cards[row].Copies;
                _ordinarySuffix[row] = _ordinarySuffix[row + 1] + (scenario.Seen[row] < 0 ? copies : 0);
                _unseenSuffix[row] = _unseenSuffix[row + 1] + copies - Math.Max(0, scenario.Seen[row]);
            }
        }

        internal BigInteger Count(int root) => Visit(root, 0, _scenario.OrdinaryCount, _scenario.HiddenCount);

        private BigInteger Visit(int node, int row, int hand, int loss) {
            _budget.Spend(1);
            if (node == 0 || hand < 0 || hand > _ordinarySuffix[row] || loss < 0 || loss > _unseenSuffix[row] - hand) {
                return 0;
            }
            if (node == 1) {
                return Multiply(_diagram.Choose(_ordinarySuffix[row], hand), _diagram.Choose(_unseenSuffix[row] - hand, loss));
            }
            if (!_needsSurvivors[node]) {
                // Once availability is irrelevant to this Boolean branch,
                // sum every hidden allocation with Vandermonde instead of
                // carrying its cardinality through the remaining hand graph.
                return Multiply(CountHand(node, row, hand), _diagram.Choose(_unseenSuffix[row] - hand, loss));
            }
            (int, int, int, int) key = (node, row, hand, loss);
            if (_memo.TryGetValue(key, out BigInteger cached)) {
                return cached;
            }
            bool ordinary = _scenario.Seen[row] < 0;
            int copies = _scenario.Cards[row].Copies;
            int minimum = ordinary ? Math.Max(0, hand - _ordinarySuffix[row + 1]) : _scenario.Held[row];
            int maximum = ordinary ? Math.Min(hand, copies) : minimum;
            BigInteger result = 0;
            int nextRelevant = _diagram._nodes[node].Level / 2;
            if (nextRelevant > row) {
                int ordinarySkipped = _ordinarySuffix[row] - _ordinarySuffix[nextRelevant];
                int unseenSkipped = _unseenSuffix[row] - _unseenSuffix[nextRelevant];
                for (int take = Math.Max(0, hand - _ordinarySuffix[nextRelevant]); take <= Math.Min(hand, ordinarySkipped); take++) {
                    for (int lost = Math.Max(0, loss - (_unseenSuffix[nextRelevant] - hand + take)); lost <= Math.Min(loss, unseenSkipped - take); lost++) {
                        Add(Multiply(_diagram.Choose(ordinarySkipped, take), _diagram.Choose(unseenSkipped - take, lost)),
                            Visit(node, nextRelevant, hand - take, loss - lost));
                    }
                }
            }
            else {
                for (int held = minimum; held <= maximum; held++) {
                    int afterHand = Advance(node, 2 * row, held);
                    if (afterHand == 0) {
                        continue;
                    }
                    int unseen = copies - (ordinary ? held : _scenario.Seen[row]);
                    int nextHand = hand - (ordinary ? held : 0);
                    BigInteger handWeight = ordinary ? _diagram.Choose(copies, held) : BigInteger.One;
                    int leastLoss = Math.Max(0, loss - (_unseenSuffix[row + 1] - nextHand));
                    for (int lost = leastLoss; lost <= Math.Min(loss, unseen); lost++) {
                        int next = Advance(afterHand, 2 * row + 1, held + lost);
                        BigInteger count = Visit(next, row + 1, nextHand, loss - lost);
                        Add(Multiply(handWeight, _diagram.Choose(unseen, lost)), count);
                    }
                }
            }
            Remember(key, result);
            return result;

            void Add(BigInteger weight, BigInteger count) {
                BigInteger increment = Multiply(weight, count);
                _budget.Spend(WorkBudget.IntegerCells(result) + WorkBudget.IntegerCells(increment) + 1);
                result += increment;
                _budget.CheckStorage(1, WorkBudget.IntegerCells(result));
            }
        }

        private BigInteger CountHand(int node, int row, int hand) {
            _budget.Spend(1);
            if (node == 0 || hand < 0 || hand > _ordinarySuffix[row]) {
                return 0;
            }
            if (node == 1) {
                return _diagram.Choose(_ordinarySuffix[row], hand);
            }
            (int, int, int, int) key = (node, row, hand, -1);
            if (_memo.TryGetValue(key, out BigInteger cached)) {
                return cached;
            }
            bool ordinary = _scenario.Seen[row] < 0;
            int copies = _scenario.Cards[row].Copies;
            int minimum = ordinary ? Math.Max(0, hand - _ordinarySuffix[row + 1]) : _scenario.Held[row];
            int maximum = ordinary ? Math.Min(hand, copies) : minimum;
            BigInteger result = 0;
            for (int held = minimum; held <= maximum; held++) {
                BigInteger count = CountHand(Advance(node, 2 * row, held), row + 1, hand - (ordinary ? held : 0));
                BigInteger increment = Multiply(ordinary ? _diagram.Choose(copies, held) : BigInteger.One, count);
                _budget.Spend(WorkBudget.IntegerCells(result) + WorkBudget.IntegerCells(increment) + 1);
                result += increment;
                _budget.CheckStorage(1, WorkBudget.IntegerCells(result));
            }
            Remember(key, result);
            return result;
        }

        private void Remember((int Node, int Row, int Hand, int Loss) key, BigInteger result) {
            // Retain recent subproblems when the bounded memo fills. Keeping
            // only the first (deepest) states prevents later roots benefiting.
            if (_memo.Count == 32768) {
                _budget.Spend(_memo.Count);
                _memo.Clear();
                _cells = 0;
            }
            _cells += 4 + WorkBudget.IntegerCells(result);
            _budget.CheckStorage(_memo.Count + 1, _cells);
            _memo.Add(key, result);
        }

        private int Advance(int node, int variable, int value) {
            _budget.Spend(1);
            Node current = _diagram._nodes[node];
            return current.Level == variable ? current.Children[value] : node;
        }

        private BigInteger Multiply(BigInteger first, BigInteger second) {
            _budget.Spend((WorkBudget.IntegerCells(first) + 1) * (WorkBudget.IntegerCells(second) + 1));
            BigInteger result = first * second;
            _budget.CheckStorage(1, WorkBudget.IntegerCells(result));
            return result;
        }
    }
}
