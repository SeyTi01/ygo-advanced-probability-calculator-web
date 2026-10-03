import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("manifest", Path(__file__).with_name("prepare-manifest.py"))
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)


class ManifestTests(unittest.TestCase):
    def test_validated_sorted_unique_image_ids_include_alternate_art(self):
        def image(i):
            return {"id": i, "image_url_small": f"https://images.ygoprodeck.com/images/cards_small/{i}.jpg"}
        result = manifest.build_manifest({"data": [
            {"card_images": [image(100000101), image(123), image(123), image(456)]},
            {"card_images": [image(0), image(True), image(2147483648),
                             {"id": 666, "image_url_small": "https://evil.test/666.jpg"}]},
            None, {"card_images": None}
        ]})
        self.assertEqual(result, {"version": 1, "variant": "small", "ids": [123, 456, 100000101]})

    def test_empty_and_oversized_allowlists_fail_closed(self):
        for catalog in [{}, {"data": [{"card_images": []}]}, {"data": [{"card_images": [
            {"id": i, "image_url_small": f"https://images.ygoprodeck.com/images/cards_small/{i}.jpg"}
            for i in range(1, 30002)]}]}]:
            with self.assertRaises(ValueError):
                manifest.build_manifest(catalog)


if __name__ == "__main__":
    unittest.main()
