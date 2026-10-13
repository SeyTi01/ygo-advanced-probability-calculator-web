import { readFileSync } from "node:fs";

export const membershipPolicy = JSON.parse(readFileSync(new URL("./archetype-policy.json", import.meta.url), "utf8"));
const normalize = text => text.replace(/[‘’]/g, "'").replace(/[“”]/g, '"').replace(/[‐‑–]/g, "-").toLowerCase();
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

export function createResolver(policy = membershipPolicy) {
    if (policy.version !== 1 || !Array.isArray(policy.candidates) || policy.candidates.length > 1000) {
        throw new Error("Invalid archetype policy");
    }
    const names = new Map();
    const candidates = policy.candidates.map(({ name, match = "token", aliases = [], caseSensitive = false }) => {
        if (typeof name !== "string" || !name.trim() || name.length > 100 || names.has(normalize(name))
            || !["token", "prefix", "suffix", "substring", "exact"].includes(match)
            || !Array.isArray(aliases) || aliases.some(alias => typeof alias !== "string" || !alias || alias.length > 100)) {
            throw new Error("Invalid or duplicate archetype candidate");
        }
        names.set(normalize(name), name);
        for (const alias of aliases) names.set(normalize(alias), name);
        const patterns = [name, ...aliases].map(value => {
            const literal = escape(caseSensitive ? value : normalize(value));
            const start = match === "exact" ? "^" : ["token", "prefix"].includes(match) ? "(?<![a-zA-Z0-9])" : "";
            const end = match === "exact" ? "$" : ["token", "suffix"].includes(match) ? "(?:s)?(?![a-zA-Z0-9])" : "";
            return new RegExp(`${start}${literal}${end}`, "u");
        });
        return { name, patterns, caseSensitive };
    });
    const matchName = name => candidates.filter(candidate => candidate.patterns.some(pattern => pattern.test(candidate.caseSensitive ? name : normalize(name)))).map(candidate => candidate.name);
    const quoted = '"[^"\\r\\n]+"';
    const list = `${quoted}(?:(?:, |,? and | and )${quoted})*`;
    const membership = new RegExp(`^This (?:card|monster) is (?:(?:also )?always (?:also )?treated|not treated|never treated) as (?:an? (${list}) (?:card|monster)|a (?:card|monster) (${quoted}))\\.$`, "i");
    const alias = new RegExp(`^This (?:card|monster)(?:'s name)? is (also )?always treated as (${quoted})\\.$`, "i");

    return card => {
        if (typeof card.name !== "string" || !card.name.trim() || (card.desc != null && (typeof card.desc !== "string" || card.desc.length > 20000))) {
            throw new Error(`Card ${card.id} has invalid identity text`);
        }
        const text = (card.desc ?? "").replace(/[‘’]/g, "'").replace(/[“”]/g, '"')
            .replace(/(?<![a-zA-Z])'([^\r\n]+?)'(?=[\s.,)])/g, '"$1"')
            .replace(/<br\s*\/?\s*>/gi, "\n");
        const additions = new Set();
        const exclusions = new Set();
        const replacements = new Set();
        const extraNames = new Set();
        const declarations = [];
        const unresolved = [];
        // Sentence/parenthesis boundaries are required, not a substring inside an effect.
        // Periods inside quoted names (U.A., F.A.) remain part of the declaration.
        const clauses = text.match(/(?:[^.()"\r\n]|"[^"\r\n]*")+(?:\.|(?=[()\r\n]|$))/g) ?? [];
        for (const raw of clauses) {
            const clause = raw.trim();
            if (!/\bthis (?:card|monster)(?:'s name)?\b.*\b(?:treated|becomes|always)\b/i.test(clause)) continue;
            if (/^This card's original (?:Level|Rank) is always treated as \d+\.$/i.test(clause)) continue;
            const member = membership.exec(clause);
            const renamed = alias.exec(clause);
            if (member) {
                const negative = /is (?:not|never) treated/i.test(clause);
                for (const value of (member[1] ?? member[2]).matchAll(/"([^"]+)"/g)) {
                    const canonical = names.get(normalize(value[1]));
                    if (!canonical) {
                        unresolved.push({ reason: "unknown-declared-archetype", excerpt: clause });
                        continue;
                    }
                    if (negative) exclusions.add(canonical);
                    else {
                        additions.add(canonical);
                        // A compound archetype name also contains its parent names.
                        for (const parent of matchName(value[1])) additions.add(parent);
                    }
                }
                declarations.push(clause);
            } else if (renamed) {
                const name = renamed[2].slice(1, -1);
                (renamed[1] ? extraNames : replacements).add(name);
                declarations.push(clause);
            } else if (/\balways\b|\b(?:not|never) treated as an? ["“]/i.test(clause)) {
                unresolved.push({ reason: "unsupported-identity-clause", excerpt: clause });
            }
        }
        if (!text.trim()) unresolved.push({ reason: "missing-description", excerpt: "" });
        if (replacements.size > 1) unresolved.push({ reason: "conflicting-name-substitutions", excerpt: [...replacements].join(" / ") });
        const effectiveNames = replacements.size === 0 ? [card.name] : replacements.size === 1 ? [...replacements] : [];
        const result = new Set([...effectiveNames, ...extraNames].flatMap(matchName));
        for (const name of additions) result.add(name);
        for (const name of exclusions) result.delete(name);
        // Missing text can hide an exclusion or a substituted name. Do not claim
        // objective membership without the complete source identity evidence.
        const archetypes = text.trim() && unresolved.length === 0 ? [...result].sort() : [];
        return { archetypes, declarations, unresolved, exclusions: [...exclusions].sort(), effectiveNames };
    };
}

export const resolveMembership = createResolver();

export function auditMembership(records, resolveCard = resolveMembership) {
    const audit = { cards: records.length, memberships: 0, withMembership: 0, declarationCards: 0, removals: [], additions: [], unresolved: [], overlaps: [] };
    for (const card of [...records].sort((a, b) => a.id - b.id)) {
        const result = resolveCard(card);
        const identity = { id: card.id, name: card.name };
        audit.memberships += result.archetypes.length;
        if (result.archetypes.length) audit.withMembership++;
        if (result.declarations.length) audit.declarationCards++;
        if (card.archetype && !result.archetypes.some(name => normalize(name) === normalize(card.archetype))) {
            audit.removals.push({ ...identity, association: card.archetype, resolved: result.archetypes });
        }
        const added = result.archetypes.filter(name => normalize(name) !== normalize(card.archetype ?? ""));
        if (added.length) audit.additions.push({ ...identity, added });
        for (const issue of result.unresolved) audit.unresolved.push({ ...identity, ...issue, excerpt: issue.excerpt.slice(0, 512) });
        if (result.archetypes.length > 1) audit.overlaps.push({ ...identity, resolved: result.archetypes });
    }
    return audit;
}
