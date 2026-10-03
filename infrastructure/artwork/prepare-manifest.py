"""Prepare metadata only; no images, secrets, credentials or provisioning.

Use --catalog for an existing response. Otherwise make one official catalog request.
Upload the output as _control/manifest.json using the R2 dashboard or operator CLI.
"""
import argparse
import json
from pathlib import Path
from urllib.request import urlopen


def build_manifest(catalog):
    ids = set()
    for card in catalog.get("data", []):
        if not isinstance(card, dict):
            continue
        images = card.get("card_images")
        if not isinstance(images, list):
            continue
        for image in images:
            if not isinstance(image, dict):
                continue
            image_id = image.get("id")
            if type(image_id) is int and 0 < image_id <= 2147483647 and image.get("image_url_small") == (
                f"https://images.ygoprodeck.com/images/cards_small/{image_id}.jpg"
            ):
                ids.add(image_id)
    if not ids or len(ids) > 30000:
        raise ValueError("Empty or oversized allowlist; review storage budget before proceeding")
    return {"version": 1, "variant": "small", "ids": sorted(ids)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.catalog:
        catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    else:
        with urlopen("https://db.ygoprodeck.com/api/v7/cardinfo.php", timeout=30) as response:
            catalog = json.load(response)
    manifest = build_manifest(catalog)
    args.output.write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
    print(f"Validated {len(manifest['ids'])} image IDs; maximum image bytes at 256 KiB each: "
          f"{len(manifest['ids']) * 262144:,} (bound, not measured storage)")
