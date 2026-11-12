import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';

const manifestSchema = 'https://agent-plugins.org/schemas/1.0.0/plugin.schema.json';
const appId = /^(?:asdk_app_|connector_|templated_apps_)[A-Za-z0-9][A-Za-z0-9_-]*$/;
const skillName = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const requireValue = (condition, message) => { if (!condition) throw new Error(message); };
const nonblank = value => typeof value === 'string' && value.trim().length > 0;
const isInside = (root, candidate) => {
    const relative = path.relative(root, candidate);
    return relative === '' || (!path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`));
};

function collectFiles(root) {
    const files = new Map();
    const visit = relative => {
        const full = path.join(root, relative);
        const stat = fs.lstatSync(full);
        requireValue(!stat.isSymbolicLink(), `Symlink is not allowed: ${relative}`);
        if (stat.isDirectory()) {
            for (const name of fs.readdirSync(full).sort()) visit(`${relative}/${name}`);
        } else {
            requireValue(stat.isFile(), `Not a regular file: ${relative}`);
            files.set(relative, fs.readFileSync(full));
        }
    };
    const rootStat = fs.lstatSync(root);
    requireValue(rootStat.isDirectory() && !rootStat.isSymbolicLink(), 'Plugin root must be a real directory');
    visit('plugin.json');
    for (const entry of ['.app.json', 'assets', 'skills']) {
        if (fs.lstatSync(path.join(root, entry), { throwIfNoEntry: false })) visit(entry);
    }
    return files;
}

function requirePackagedReference(root, files, from, target) {
    const pathname = decodeURIComponent(target.split(/[?#]/, 1)[0]);
    if (!pathname) return;
    requireValue(!path.isAbsolute(pathname) && !path.win32.isAbsolute(pathname), `Absolute reference in ${from}: ${target}`);
    const full = path.resolve(root, path.dirname(from), pathname);
    requireValue(isInside(root, full), `Reference escapes plugin in ${from}: ${target}`);
    const relative = path.relative(root, full).split(path.sep).join('/');
    requireValue(files.has(relative), `Missing packaged reference in ${from}: ${target}`);
}

function readSkillFrontmatter(text, filename) {
    const match = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/.exec(text);
    requireValue(match, `Missing YAML frontmatter: ${filename}`);
    const fields = {};
    // The package deliberately uses only these two scalar YAML fields. JSON-quoted
    // descriptions are valid YAML and can be parsed without an extra dependency.
    for (const line of match[1].split(/\r?\n/).filter(line => line.trim())) {
        const field = /^(name|description):\s*(.+)$/.exec(line);
        requireValue(field && !(field[1] in fields), `Unsupported or duplicate frontmatter field: ${filename}`);
        fields[field[1]] = field[2].startsWith('"') ? JSON.parse(field[2]) : field[2];
        requireValue(field[1] !== 'description' || field[2].startsWith('"'), `Quote the description as a JSON string: ${filename}`);
    }
    return fields;
}

export function validatePackage(directory) {
    const root = path.resolve(directory);
    const files = collectFiles(root);
    const manifest = JSON.parse(files.get('plugin.json').toString('utf8'));
    requireValue(manifest.$schema === manifestSchema, 'Invalid plugin manifest schema');
    requireValue(typeof manifest.name === 'string' && manifest.name.length <= 64 && /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(manifest.name), 'Invalid plugin name');
    const version = typeof manifest.version === 'string' && /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/.exec(manifest.version);
    requireValue(version && (!version[4] || version[4].split('.').every(part => !/^\d+$/.test(part) || part === '0' || !part.startsWith('0'))), 'Invalid plugin version');
    const openai = manifest.extensions?.['com.openai'];
    requireValue(nonblank(openai?.interface?.displayName), 'Missing plugin display name');
    requireValue(nonblank(openai.interface.shortDescription) && openai.interface.shortDescription.length <= 30, 'Plugin shortDescription must contain 1–30 characters');
    for (const field of ['logo', 'composerIcon', 'logoDark', 'composerIconDark']) {
        if (openai.interface[field]) requirePackagedReference(root, files, 'plugin.json', openai.interface[field]);
    }
    if (openai.apps) {
        requireValue(openai.apps === './.app.json', 'App dependencies must use ./.app.json');
        requireValue(files.has('.app.json'), 'Missing .app.json');
    }
    if (files.has('.app.json')) {
        requireValue(openai.apps === './.app.json', 'Manifest must declare .app.json');
        const apps = JSON.parse(files.get('.app.json').toString('utf8')).apps;
        requireValue(apps && !Array.isArray(apps) && typeof apps === 'object' && Object.keys(apps).length > 0, 'Invalid app dependency map');
        for (const [key, app] of Object.entries(apps)) {
            requireValue(nonblank(key) && appId.test(app?.id ?? ''), `Invalid app dependency: ${key}`);
            requireValue((app.required === undefined || app.required === true) && (app.optional === undefined || app.optional === false), `App dependencies cannot be optional: ${key}`);
        }
    }
    const names = new Set();
    for (const [filename, bytes] of files) {
        if (!filename.endsWith('.md')) continue;
        const text = bytes.toString('utf8');
        const skill = /^skills\/([^/]+)\/SKILL\.md$/.exec(filename);
        requireValue(!filename.endsWith('/SKILL.md') || skill, `Skill must be directly under skills/<name>: ${filename}`);
        if (skill) {
            const fields = readSkillFrontmatter(text, filename);
            requireValue(skillName.test(fields.name ?? '') && fields.name.length <= 64 && fields.name === skill[1] && !names.has(fields.name), `Invalid or duplicate skill name: ${filename}`);
            requireValue(nonblank(fields.description), `Missing skill description: ${filename}`);
            names.add(fields.name);
        }
        const prose = text.replace(/^(`{3,}|~{3,})[^\n]*\n[\s\S]*?^\1\s*$/gm, '').replace(/`[^`\n]+`/g, '');
        for (const match of prose.matchAll(/!?\[[^\]\n]*\]\(\s*(<[^>\n]+>|[^\s)]+)(?:\s+"[^"]*")?\s*\)/g)) {
            const target = match[1].replace(/^<|>$/g, '');
            if (/^(?:https?:|mailto:|#)/i.test(target)) continue;
            requirePackagedReference(root, files, filename, target);
        }
    }
    requireValue(names.size > 0, 'Package contains no skills');
    return { root, manifest, files, skillCount: names.size };
}

function tarHeader(filename, size) {
    const header = Buffer.alloc(512);
    let name = filename;
    let prefix = '';
    if (Buffer.byteLength(name) > 100) {
        const split = [...filename.matchAll(/\//g)].map(match => match.index).reverse()
            .find(index => Buffer.byteLength(filename.slice(0, index)) <= 155 && Buffer.byteLength(filename.slice(index + 1)) <= 100);
        requireValue(split !== undefined, `Archive path is too long: ${filename}`);
        prefix = filename.slice(0, split);
        name = filename.slice(split + 1);
    }
    header.write(name, 0, 100);
    const octal = (value, offset, length) => {
        const encoded = value.toString(8).padStart(length - 1, '0');
        requireValue(encoded.length < length, 'Archive numeric field overflow');
        header.write(`${encoded}\0`, offset, length);
    };
    octal(0o644, 100, 8);
    octal(0, 108, 8);
    octal(0, 116, 8);
    octal(size, 124, 12);
    octal(0, 136, 12);
    header.fill(32, 148, 156);
    header.write('0', 156);
    header.write('ustar\0', 257, 6);
    header.write('00', 263, 2);
    header.write(prefix, 345, 155);
    const checksum = header.reduce((sum, byte) => sum + byte, 0);
    header.write(`${checksum.toString(8).padStart(6, '0')}\0 `, 148, 8);
    return header;
}

export function encodeArchive(name, files) {
    const chunks = [];
    for (const [filename, contents] of files) {
        chunks.push(tarHeader(`${name}/${filename}`, contents.length), contents, Buffer.alloc((512 - contents.length % 512) % 512));
    }
    chunks.push(Buffer.alloc(1024));
    return gzipSync(Buffer.concat(chunks));
}

export function inspectArchive(archive) {
    const tar = gunzipSync(archive);
    const files = new Map();
    const stringField = (header, start, size) => header.subarray(start, start + size).toString('utf8').replace(/\0.*$/s, '');
    let offset = 0;
    while (offset + 512 <= tar.length) {
        const header = tar.subarray(offset, offset + 512);
        if (header.every(byte => byte === 0)) {
            requireValue(tar.length - offset >= 1024 && tar.subarray(offset).every(byte => byte === 0), 'Invalid archive terminator');
            return files;
        }
        const checksum = header.reduce((sum, byte, index) => sum + (index >= 148 && index < 156 ? 32 : byte), 0);
        requireValue(Number.parseInt(stringField(header, 148, 8), 8) === checksum, 'Archive checksum mismatch');
        requireValue(stringField(header, 257, 6) === 'ustar' && header[156] === 48, 'Unsupported archive entry');
        const prefix = stringField(header, 345, 155);
        const filename = `${prefix ? `${prefix}/` : ''}${stringField(header, 0, 100)}`;
        requireValue(!filename.startsWith('/') && !/[:\\]/.test(filename) && filename.split('/').every(part => part && part !== '.' && part !== '..'), 'Unsafe archive path');
        requireValue(!files.has(filename), `Duplicate archive entry: ${filename}`);
        const sizeText = stringField(header, 124, 12);
        requireValue(/^[0-7]+$/.test(sizeText), 'Invalid archive size');
        const size = Number.parseInt(sizeText, 8);
        requireValue(offset + 512 + size <= tar.length, 'Truncated archive entry');
        files.set(filename, tar.subarray(offset + 512, offset + 512 + size));
        offset += 512 + Math.ceil(size / 512) * 512;
    }
    throw new Error('Missing archive terminator');
}

export function packagePlugin(directory, output) {
    const validated = validatePackage(directory);
    requireValue(path.isAbsolute(output) && output.endsWith('.tar.gz'), 'Output must be an absolute .tar.gz path');
    requireValue(!isInside(validated.root, output), 'Archive must be outside the plugin source');
    fs.mkdirSync(path.dirname(output), { recursive: true });
    fs.writeFileSync(output, encodeArchive(validated.manifest.name, validated.files));
    const archived = inspectArchive(fs.readFileSync(output));
    requireValue(archived.size === validated.files.size, 'Archive file count mismatch');
    for (const [filename, contents] of validated.files) {
        requireValue(archived.get(`${validated.manifest.name}/${filename}`)?.equals(contents), `Archive content mismatch: ${filename}`);
    }
    return { ...validated, output };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        requireValue(process.argv.length === 3, 'Usage: node scripts/package.mjs --check | <absolute-output.tar.gz>');
        const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
        const result = process.argv[2] === '--check' ? validatePackage(root) : packagePlugin(root, process.argv[2]);
        console.log(JSON.stringify({ name: result.manifest.name, version: result.manifest.version, skills: result.skillCount, files: result.files.size, ...(result.output ? { archive: result.output } : {}) }));
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
