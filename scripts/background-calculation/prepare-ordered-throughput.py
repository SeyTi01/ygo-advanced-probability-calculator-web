"""Freeze a paired matrix from the existing Fast, pressure and physical exporters.
Usage: python prepare-ordered-throughput.py fast.json pressure.json physical.json output.json
The same output (and its SHA-256) must be used on both revisions.
"""
import copy
import json
import sys

fast, pressure, physical = [json.load(open(path, encoding="utf-8")) for path in sys.argv[1:4]]
matrix = fast + pressure
for case in matrix:
    case["Unchanged"] = case["Name"] not in {
        "ordered-target-loss-40", "deferred-target-loss-40", "vs-k9-ordered-40"
    } and not case["Name"].startswith("ordered-storage-")
for case in physical:
    if case["Name"] not in {"ordered-12", "ordered-40", "selector-edge-8", "selector-edge-9", "vs-k9-ordered-40"}:
        continue
    for mode in [0, 2]:
        if case["Name"] == "vs-k9-ordered-40" and mode == 0:
            continue  # The maintained Fast exporter defines the headline input.
        item = copy.deepcopy(case)
        item["Name"] = case["Name"] + ("-fast" if mode == 0 else "-exact")
        item["Input"]["Options"]["Mode"] = mode
        item["Input"]["WorkUnits"] = 150_000_000 if mode == 0 else case["Input"].get("WorkUnits", 10_000_000)
        item["Unchanged"] = mode == 2
        matrix.append(item)
        if case["Name"].startswith("selector-edge-"):
            uneven = copy.deepcopy(item)
            uneven["Name"] += "-uneven"
            rows = uneven["Input"]["Cards"]
            rows[0]["Copies"] += 1
            next(row for row in rows if row["Id"] == "f")["Copies"] -= 1
            matrix.append(uneven)
with open(sys.argv[4], "w", encoding="utf-8") as file:
    json.dump(matrix, file, separators=(",", ":"))
