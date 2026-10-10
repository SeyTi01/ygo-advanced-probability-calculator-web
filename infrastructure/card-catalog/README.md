# Local card search catalog

`prepare-catalog.mjs` projects the official YGOPRODeck full catalog into the compact `CardInfo` JSON asset used by local search. It keeps card identity, name, objective metadata, and numeric artwork IDs. It drops descriptions, prices, set data, image URLs, and other provider fields; it never fetches artwork.

Refresh from an already downloaded official response without network access:

```sh
node infrastructure/card-catalog/prepare-catalog.mjs --catalog cardinfo.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json
```

Omitting `--catalog` makes one full metadata request. The catalog is validated and serialized before the output is atomically replaced. The normal app build and test suite never call the provider.

The same saved provider response can also refresh the artwork allowlist:

```sh
python infrastructure/artwork/prepare-manifest.py --catalog cardinfo.json --output manifest.json
```

Run the generator's offline tests with:

```sh
node --test infrastructure/card-catalog/prepare-catalog.test.mjs
```
