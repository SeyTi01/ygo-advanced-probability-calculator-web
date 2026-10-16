"""Build a frozen adaptive-routing matrix from FastCertificateBenchmark exports.

Args: fixtures.json coverage.json output.json. Run the exporter on unchanged dev;
keep the exported inputs and baseline responses when replaying the candidate.
"""
import copy
import json
import sys

fixtures, coverage = [json.load(open(path, encoding="utf-8")) for path in sys.argv[1:3]]
for name in ["multiple-draw-opt-duplicates", "two-draws-custom-priority",
             "solar-style-cost-and-mill", "maximum-only-removal", "search-only",
             "minimal-hidden-only-target"]:
    fixture = copy.deepcopy(next(value for value in coverage if value["Name"] == name))
    fixture["Physical"] = True
    fixtures.append(fixture)
base = next(value for value in fixtures if value["Name"] == "minimal-filtered-target-discard")
for entries in [8, 9]:
    fixture = copy.deepcopy(base)
    fixture["Name"] = f"distinct-{entries}-40"
    fixture["Input"]["HandSize"] = 5
    cards = fixture["Input"]["Cards"]
    cards[2]["Copies"] = 1
    for index in range(entries - len(cards)):
        card = copy.deepcopy(cards[2])
        card["Id"] = "blank" + str(index)
        cards.append(card)
    cards[2]["Copies"] += 40 - sum(card["Copies"] for card in cards)
    fixture["Physical"] = True
    fixtures.append(fixture)
    balanced = copy.deepcopy(fixture)
    balanced["Name"] = f"balanced-{entries}-40"
    balanced["Input"]["WorkUnits"] = 10_000_000
    for index, card in enumerate(balanced["Input"]["Cards"]):
        card["Copies"] = 40 // entries + (index < 40 % entries)
    fixtures.append(balanced)
for kind in ["many-copies", "many-groups", "many-alternatives", "low-work"]:
    fixture = copy.deepcopy(base)
    fixture["Name"] = kind
    fixture["Physical"] = True
    request = fixture["Input"]
    if kind == "many-copies":
        request["Cards"][2]["Copies"] = 37
    if kind == "many-groups":
        request["Groups"] *= 5
    if kind == "many-alternatives":
        alternative = {"Kind": "Category", "Category": request["Combos"][0]["Categories"][0], "Card": None}
        request["Combos"][0]["AlternativeGroups"] = [{"Alternatives": [alternative] * 8}]
    if kind == "low-work":
        request["WorkUnits"] = 10_000
    fixtures.append(fixture)
with open(sys.argv[3], "w", encoding="utf-8") as output:
    json.dump(fixtures, output)
