import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const manifestPath = resolve(repositoryRoot, 'YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json');
export const localeRegistry = JSON.parse(readFileSync(manifestPath, 'utf8'));
export const localeIdPattern = /^[a-z]{2,3}(-[a-z0-9]{2,8})*$/i;
export const isValidLocaleId = id => typeof id === 'string' && localeIdPattern.test(id);

if (!Array.isArray(localeRegistry) || localeRegistry.length === 0) throw new Error('Invalid production locale registry');
const ids = new Set();
for (const locale of localeRegistry) {
    if (!locale || !isValidLocaleId(locale.id)
        || typeof locale.name !== 'string' || !locale.name.trim() || ids.has(locale.id)) {
        throw new Error('Invalid production locale registry');
    }
    ids.add(locale.id);
    if (locale.cardNames !== undefined) {
        const expectedAsset = `data/card-names.${locale.id}.v1.json`;
        if (locale.cardNames === null || locale.cardNames.asset !== expectedAsset) {
            throw new Error(`Invalid reviewed card-name asset for locale: ${locale.id}`);
        }
    }
}

export const cardNameLocales = localeRegistry.filter(locale => locale.cardNames).map(locale => locale.id);
export const cardNameLocaleSet = new Set(cardNameLocales);
export const cardNameAssetPaths = new Map(localeRegistry.filter(locale => locale.cardNames)
    .map(locale => [locale.id, `YGOProbabilityCalculatorBlazor/wwwroot/${locale.cardNames.asset}`]));

export function getCardNameLocale(locale) {
    if (!cardNameLocaleSet.has(locale)) throw new Error('Unsupported name locale');
    return locale;
}
