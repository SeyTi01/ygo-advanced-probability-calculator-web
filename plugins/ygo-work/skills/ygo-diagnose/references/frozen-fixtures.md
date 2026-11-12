# Frozen calculation fixtures

Keep baseline requests and responses unchanged across revisions. Use the current public wire; hand-built model projections can silently drop newer card fields or execution policies.

Files are JSON arrays of `{ Name, Json, Execution?, ReferenceResponse }`:

- `Json`: exact serialized `CalculationInput`; preserve its bytes across revisions.
- `Execution`: absent/null for the finite direct worker path, or `interactive-exact-v1` for the UI's unlimited-work Exact path.
- `ReferenceResponse`: complete baseline response, including typed failures. Generate it on the baseline; candidate replay must never replace it.

Native replay compares the whole response. Browser replay requires exact fractions, keys and integer fields; noninteger doubles allow four machine-epsilon units of relative rounding.

Replay artifacts record request hashes, native assembly or published-asset identity, first/warm samples, full replies and parity failures.
