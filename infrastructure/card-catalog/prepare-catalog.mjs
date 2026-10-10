#!/usr/bin/env node
// Build the calculator's compact local card-search catalog from a YGOPRODeck response.
// Pass --catalog with an existing official catalog JSON file for offline operation.
// Without it, the tool makes one full metadata request and never downloads artwork.

import { mkdir, readFile, rename, rm, stat, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const CATALOG_URL = "https://db.ygoprodeck.com/api/v7/cardinfo.php";
export const MAX_INPUT_BYTES = 100 * 1024 * 1024;
export const MAX_OUTPUT_BYTES = 20 * 1024 * 1024;
export const MAX_CARD_COUNT = 30_000;
export const MIN_FULL_CATALOG_CARDS = 1_000;
const MAX_TEXT_LENGTH = 512;
const MAX_NUMERIC_VALUE = 1_000;

export function projectCard(card) {
    if (!isRecord(card)) {
        throw new Error("Every catalog record must be an object");
    }

    const cardId = card.id;
    const name = card.name;
    const cardType = card.type;
    if (!Number.isInteger(cardId) || cardId <= 0 || cardId > 2_147_483_647) {
        throw new Error("Every card must have a positive 32-bit integer id");
    }
    if (typeof name !== "string" || !name.trim() || name.length > MAX_TEXT_LENGTH) {
        throw new Error(`Card ${cardId} has an invalid name`);
    }
    if (typeof cardType !== "string" || !cardType.trim() || cardType.length > MAX_TEXT_LENGTH) {
        throw new Error(`Card ${cardId} has an invalid type`);
    }

    const projected = { id: cardId, name, type: cardType };
    for (const property of ["frameType", "race", "attribute", "archetype"]) {
        const value = card[property];
        if (value === undefined || value === null) {
            continue;
        }
        if (typeof value !== "string" || value.length > MAX_TEXT_LENGTH) {
            throw new Error(`Card ${cardId} has an invalid ${property}`);
        }
        projected[property] = value;
    }

    for (const [source, destination] of [["level", "level"], ["linkval", "linkVal"], ["scale", "scale"]]) {
        const value = card[source];
        if (value === undefined || value === null) {
            continue;
        }
        if (!Number.isInteger(value) || value < 0 || value > MAX_NUMERIC_VALUE) {
            throw new Error(`Card ${cardId} has an invalid ${source}`);
        }
        projected[destination] = value;
    }

    const images = card.card_images ?? [];
    if (!Array.isArray(images)) {
        throw new Error(`Card ${cardId} has invalid artwork metadata`);
    }

    const artworkImageIds = [];
    const seenIds = new Set();
    for (const image of images) {
        if (!isRecord(image)) {
            throw new Error(`Card ${cardId} has a malformed artwork record`);
        }
        const imageId = image.id;
        if (!Number.isInteger(imageId) || imageId <= 0 || imageId > 2_147_483_647) {
            throw new Error(`Card ${cardId} has an invalid artwork id`);
        }
        if (!seenIds.has(imageId)) {
            artworkImageIds.push(imageId);
            seenIds.add(imageId);
        }
    }

    projected.canonicalCardId = cardId;
    projected.artworkImageIds = artworkImageIds;
    projected.artworkMetadataKnown = true;
    return projected;
}

export function buildCatalog(response, minimumCards = 1) {
    if (!isRecord(response) || !Array.isArray(response.data)) {
        throw new Error("Expected an official catalog object with a data array");
    }

    const records = response.data;
    if (records.length < minimumCards || records.length > MAX_CARD_COUNT) {
        throw new Error("Catalog is empty, incomplete, or larger than the supported limit");
    }

    const cards = records.map(projectCard);
    const ids = new Set(cards.map(card => card.id));
    if (ids.size !== cards.length) {
        throw new Error("Catalog contains duplicate card ids");
    }

    cards.sort((left, right) => compareText(left.name.toLowerCase(), right.name.toLowerCase())
        || compareText(left.name, right.name)
        || left.id - right.id);
    return { version: 1, cards };
}

export function serializeCatalog(catalog) {
    const output = Buffer.from(`${JSON.stringify(catalog)}\n`, "utf8");
    if (output.length === 0 || output.length > MAX_OUTPUT_BYTES) {
        throw new Error("Generated catalog is empty or exceeds the output size limit");
    }
    return output;
}

export async function readCatalogBytes(path) {
    const metadata = await stat(path);
    if (metadata.size > MAX_INPUT_BYTES) {
        throw new Error("Input catalog exceeds the 100 MiB size limit");
    }
    const content = await readFile(path);
    if (content.length === 0 || content.length > MAX_INPUT_BYTES) {
        throw new Error("Input catalog is empty or exceeds the 100 MiB size limit");
    }
    return content;
}

export async function downloadCatalog() {
    const response = await fetch(CATALOG_URL, { signal: AbortSignal.timeout(60_000) });
    if (!response.ok) {
        throw new Error(`Official catalog request failed with HTTP ${response.status}`);
    }
    const contentLength = Number(response.headers.get("content-length"));
    if (Number.isFinite(contentLength) && contentLength > MAX_INPUT_BYTES) {
        throw new Error("Official catalog response exceeds the 100 MiB size limit");
    }

    const chunks = [];
    let totalLength = 0;
    for await (const chunk of response.body) {
        totalLength += chunk.length;
        if (totalLength > MAX_INPUT_BYTES) {
            throw new Error("Official catalog response exceeds the 100 MiB size limit");
        }
        chunks.push(chunk);
    }
    if (totalLength === 0) {
        throw new Error("Official catalog response is empty");
    }
    return Buffer.concat(chunks, totalLength);
}

export async function writeAtomically(path, content) {
    await mkdir(dirname(path), { recursive: true });
    const temporaryPath = `${path}.tmp`;
    try {
        await writeFile(temporaryPath, content);
        await rename(temporaryPath, path);
    }
    finally {
        await rm(temporaryPath, { force: true });
    }
}

function parseArguments(args) {
    const options = {};
    for (let index = 0; index < args.length; index++) {
        const key = args[index];
        if (key !== "--catalog" && key !== "--output") {
            throw new Error(`Unknown argument: ${key}`);
        }
        const value = args[++index];
        if (!value) {
            throw new Error(`Missing value for ${key}`);
        }
        options[key.slice(2)] = value;
    }
    if (!options.output) {
        throw new Error("--output is required");
    }
    return options;
}

async function main() {
    const options = parseArguments(process.argv.slice(2));
    const input = options.catalog ? await readCatalogBytes(options.catalog) : await downloadCatalog();
    const text = input.toString("utf8").replace(/^\uFEFF/, "");
    const source = JSON.parse(text);
    const catalog = buildCatalog(source, MIN_FULL_CATALOG_CARDS);
    const output = serializeCatalog(catalog);
    await writeAtomically(resolve(options.output), output);
    process.stdout.write(`Wrote ${catalog.cards.length.toLocaleString("en-US")} cards (${output.length.toLocaleString("en-US")} bytes) to ${options.output}\n`);
}

function isRecord(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

function compareText(left, right) {
    return left < right ? -1 : left > right ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    main().catch(error => {
        process.stderr.write(`${error.message}\n`);
        process.exitCode = 1;
    });
}
