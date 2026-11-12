import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { gzipSync, gunzipSync } from 'node:zlib';
import { validatePackage, packagePlugin, inspectArchive } from './package.mjs';

function fixture(t) {
    const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'ygo-plugin-package-'));
    const root = path.join(temporary, 'source');
    const write = (filename, contents) => {
        fs.mkdirSync(path.dirname(path.join(root, filename)), { recursive: true });
        fs.writeFileSync(path.join(root, filename), contents);
    };
    t.after(() => fs.rmSync(temporary, { recursive: true, force: true }));
    const manifest = {
        $schema: 'https://agent-plugins.org/schemas/1.0.0/plugin.schema.json',
        name: 'ygo-work', version: '1.0.0',
        extensions: { 'com.openai': { apps: './.app.json', interface: { displayName: 'YGO Work', shortDescription: 'Repository workflows', logo: './assets/icon.svg' } } }
    };
    write('plugin.json', JSON.stringify(manifest));
    write('.app.json', JSON.stringify({ apps: { github: { id: 'connector_verified_fixture', required: true } } }));
    write('assets/icon.svg', '<svg xmlns="http://www.w3.org/2000/svg"/>');
    write('skills/ygo-plan/SKILL.md', '---\nname: ygo-plan\ndescription: "Plan work."\n---\nRead [details](references/details.md).\n');
    write('skills/ygo-plan/references/details.md', '# Details\nUse `repo:README.md`.\n');
    return { root, temporary, write, manifest };
}

test('archive round trip preserves hidden manifest, UTF-8 content, long paths and excludes tooling', t => {
    const f = fixture(t);
    f.write('scripts/private.txt', 'Do not ship');
    f.write('unrelated.txt', 'Do not ship');
    f.write(`skills/ygo-plan/references/${'long-name-'.repeat(8)}.md`, '# Grüße\n');
    const output = path.join(f.temporary, 'result.tar.gz');
    const result = packagePlugin(f.root, output);
    const archived = inspectArchive(fs.readFileSync(output));
    assert.equal(result.skillCount, 1);
    assert.equal(archived.size, 6);
    assert.ok(archived.has('ygo-work/.app.json'));
    for (const [filename, data] of result.files) assert.deepEqual(archived.get(`ygo-work/${filename}`), data);
    assert.ok([...archived.keys()].every(filename => !filename.includes('private') && !filename.includes('unrelated')));
});

test('rejects missing or escaping local links before packaging', t => {
    const f = fixture(t);
    const filename = 'skills/ygo-plan/references/details.md';
    f.write(filename, '[missing](absent.md)');
    assert.throws(() => validatePackage(f.root), /Missing packaged reference/);
    f.write(filename, '[escape](../../../../outside.md)');
    assert.throws(() => validatePackage(f.root), /Reference escapes plugin/);
    f.write(filename, '[escape](%2Fetc%2Fpasswd)');
    assert.throws(() => validatePackage(f.root), /Absolute reference/);
});

test('ignores external links and Markdown examples in code', t => {
    const f = fixture(t);
    f.write('skills/ygo-plan/references/details.md', '[docs](https://example.com/docs)\n[section](#section)\n`[inline](missing.md)`\n```md\n[example](missing.md)\n```\n');
    assert.equal(validatePackage(f.root).skillCount, 1);
});

test('rejects malformed manifest, plugin IDs passed as app IDs, and invalid skills', t => {
    const f = fixture(t);
    f.manifest.extensions['com.openai'].interface.shortDescription = 'x'.repeat(31);
    f.write('plugin.json', JSON.stringify(f.manifest));
    assert.throws(() => validatePackage(f.root), /shortDescription/);
    f.manifest.extensions['com.openai'].interface.shortDescription = 'Work';
    f.manifest.version = '1.0.0-01';
    f.write('plugin.json', JSON.stringify(f.manifest));
    assert.throws(() => validatePackage(f.root), /plugin version/);
    f.manifest.version = '1.0.0';
    f.write('plugin.json', JSON.stringify(f.manifest));
    f.write('.app.json', JSON.stringify({ apps: { github: { id: 'plugin_connector_wrong' } } }));
    assert.throws(() => validatePackage(f.root), /Invalid app dependency/);
    f.write('.app.json', JSON.stringify({ apps: { github: { id: 'connector_verified_fixture', required: false } } }));
    assert.throws(() => validatePackage(f.root), /cannot be optional/);
    f.write('.app.json', JSON.stringify({ apps: { github: { id: 'connector_verified_fixture', optional: true } } }));
    assert.throws(() => validatePackage(f.root), /cannot be optional/);
    f.write('.app.json', JSON.stringify({ apps: { github: { id: 'connector_verified_fixture' } } }));
    f.write('skills/ygo-plan/SKILL.md', '---\nname: ygo-other\ndescription: "Plan work."\n---\n');
    assert.throws(() => validatePackage(f.root), /skill name/);
    f.write('skills/ygo-plan/SKILL.md', '---\nname: ygo-plan\ndescription: ""\n---\n');
    assert.throws(() => validatePackage(f.root), /skill description/);
});

test('rejects symlinked packaged directories without following them', t => {
    const f = fixture(t);
    const external = path.join(f.temporary, 'external');
    fs.mkdirSync(external);
    fs.writeFileSync(path.join(external, 'secret.txt'), 'Do not read');
    fs.symlinkSync(external, path.join(f.root, 'assets', 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
    assert.throws(() => validatePackage(f.root), /Symlink is not allowed/);
});

test('refuses output inside source and catches damaged archive headers', t => {
    const f = fixture(t);
    assert.throws(() => packagePlugin(f.root, path.join(f.root, 'result.tar.gz')), /outside the plugin source/);
    const output = path.join(f.temporary, 'result.tar.gz');
    packagePlugin(f.root, output);
    const tar = gunzipSync(fs.readFileSync(output));
    tar[0] ^= 1;
    assert.throws(() => inspectArchive(gzipSync(tar)), /checksum mismatch/);
});
