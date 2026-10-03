// Local publication guard. Policy and backups stay in the shared Git directory.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const marker = '# ygo-publication-guard-v1';
const events = ['pre-commit', 'commit-msg', 'pre-push'];
function fail(reason) { throw new Error(reason); }
function git(args, input) {
    const result = spawnSync('git', args, { encoding: 'utf8', input, env: { ...process.env, GIT_NO_REPLACE_OBJECTS: '1' } });
    if (result.error || result.status !== 0) fail('Git validation unavailable; resolve access/history and retry.');
    return result.stdout.trimEnd();
}
function commonDir() { return path.resolve(git(['rev-parse', '--git-common-dir'])); }
function policyDir() { return path.join(commonDir(), 'privacy-guard'); }
function loadPolicy() {
    const policy = JSON.parse(fs.readFileSync(path.join(policyDir(), 'policy.json'), 'utf8'));
    if (!Array.isArray(policy.identities) || !policy.identities.length) {
        fail('No approved public identity configured. Run the documented explicit identity setup.');
    }
    return policy;
}
function approved(name, email, policy) {
    if (!policy.identities.some(pair => pair.name === name && pair.email === email)) {
        fail('Unapproved identity rejected (values redacted). Configure an approved public identity; do not rewrite others or bypass hooks.');
    }
}
function ident(value, policy) {
    const match = /^([^<>\r\n]+) <([^<>\r\n]+)> \d+ [+-]\d{4}$/.exec(value);
    if (!match) fail('Unexpected identity metadata rejected (values redacted).');
    approved(match[1], match[2], policy);
}
function effective(policy) {
    ident(git(['var', 'GIT_AUTHOR_IDENT']), policy);
    ident(git(['var', 'GIT_COMMITTER_IDENT']), policy);
}
function trailers(message, policy) {
    // Inspect identity-bearing lines even if malformed placement prevents Git
    // from classifying them as the final trailer block.
    for (const line of message.split(/\r?\n/)) {
        const trailer = /^\s*(?:signed-off-by|co-authored-by)\s*:\s*(.*)$/i.exec(line);
        if (!trailer) continue;
        const match = /^([^<>]+) <([^<>]+)>$/.exec(trailer[1]);
        if (!match) fail('Malformed identity trailer rejected (values redacted).');
        approved(match[1], match[2], policy);
    }
}
function commit(oid, policy) {
    const raw = git(['cat-file', 'commit', oid]);
    const separator = raw.indexOf('\n\n');
    if (separator < 0) fail('Malformed commit rejected.');
    const headers = raw.slice(0, separator).split('\n');
    for (const role of ['author', 'committer']) {
        const fields = headers.filter(line => line.startsWith(role + ' '));
        if (fields.length !== 1) fail('Unexpected commit identity metadata rejected.');
        ident(fields[0].slice(role.length + 1), policy);
    }
    trailers(raw.slice(separator + 2), policy);
}
function peeledCommit(oid, policy, published, inspectTags) {
    const seen = new Set();
    while (git(['cat-file', '-t', oid]) === 'tag') {
        if (seen.has(oid)) fail('Ambiguous tag chain rejected.');
        seen.add(oid);
        const raw = git(['cat-file', 'tag', oid]);
        const separator = raw.indexOf('\n\n');
        const headers = raw.slice(0, separator).split('\n');
        if (inspectTags && !published.has(oid)) {
            const taggers = headers.filter(line => line.startsWith('tagger '));
            if (taggers.length !== 1) fail('Missing tagger identity rejected.');
            ident(taggers[0].slice(7), policy);
            trailers(raw.slice(separator + 2), policy);
        }
        const object = headers.find(line => line.startsWith('object '));
        if (!object) fail('Malformed annotated tag rejected.');
        oid = object.slice(7);
    }
    if (git(['cat-file', '-t', oid]) !== 'commit') fail('Only commit-backed branch/tag publication is supported.');
    return oid;
}
function push(remoteUrl, input, policy) {
    const grafts = path.resolve(git(['rev-parse', '--git-path', 'info/grafts']));
    if (fs.existsSync(grafts) && fs.readFileSync(grafts, 'utf8').trim()) {
        fail('Grafted history cannot establish the publication range. Inspect unmodified history before publishing.');
    }
    if (git(['rev-parse', '--is-shallow-repository']) !== 'false') {
        fail('Shallow history cannot be audited. Fetch complete history before publishing.');
    }
    const updates = input.trim() ? input.trim().split(/\r?\n/).map(line => {
        const fields = line.split(/\s+/);
        if (fields.length !== 4 || !fields.slice(1, 2).concat(fields.slice(3)).every(oid => /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(oid))) {
            fail('Ambiguous push update rejected.');
        }
        return { local: fields[1], ref: fields[2], old: fields[3] };
    }) : [];
    if (!updates.some(update => !/^0+$/.test(update.local))) return;
    const advertised = git(['ls-remote', '--refs', remoteUrl, 'refs/heads/*', 'refs/tags/*']);
    const refs = new Map(advertised.split('\n').filter(Boolean).map(line => {
        const [oid, ref] = line.split(/\s+/);
        if (!/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(oid) || !ref) fail('Ambiguous remote advertisement rejected.');
        return [ref, oid];
    }));
    const published = new Set(refs.values());
    // The actual remote's advertised heads/tags are the publication boundary,
    // never a cached tracking ref, a date, HEAD alone or a fixed commit count.
    const exclusions = new Set([...published].map(oid => peeledCommit(oid, policy, published, false)));
    const tips = new Set();
    for (const update of updates) {
        if ((refs.get(update.ref) ?? '0'.repeat(update.old.length)) !== update.old) {
            fail('Remote changed during validation; refresh and retry without bypassing the guard.');
        }
        if (/^0+$/.test(update.local)) continue;
        if (!/^refs\/(heads|tags)\//.test(update.ref)) fail('Unsupported ref publication rejected.');
        tips.add(peeledCommit(update.local, policy, published, true));
    }
    const range = [...tips, ...[...exclusions].map(oid => '^' + oid)].join('\n') + '\n';
    for (const oid of git(['rev-list', '--stdin'], range).split('\n').filter(Boolean)) commit(oid, policy);
}
function previousHook(event, args, input) {
    const prior = path.join(commonDir(), 'hooks', event + '.privacy-original');
    if (!fs.existsSync(prior)) return;
    // Git's shell retains shebang/native execution semantics. The exact original
    // arguments and complete pre-push stdin are forwarded once, unchanged.
    const result = spawnSync('sh', ['-c', 'hook=$1; shift; exec "$hook" "$@"', 'privacy-original', prior, ...args], {
        input, stdio: ['pipe', 'inherit', 'inherit']
    });
    if (result.error) fail('Existing hook could not execute; publication stopped.');
    if (result.status !== 0) process.exit(result.status ?? 1);
}
function shellQuote(text) { return "'" + text.replaceAll("'", "'\\''") + "'"; }
function install(args) {
    const nameIndex = args.indexOf('--name');
    const emailIndex = args.indexOf('--email');
    if ((nameIndex < 0) !== (emailIndex < 0)) fail('Supply both approved public name and email explicitly.');
    const identities = nameIndex >= 0 ? [{ name: args[nameIndex + 1], email: args[emailIndex + 1] }] : null;
    if (identities && identities.some(pair => !pair.name || !/^[^<>\r\n]+$/.test(pair.name) || !/^[^<>\s]+@[^<>\s]+$/.test(pair.email))) {
        fail('Approved identity input is malformed (values redacted).');
    }
    const common = commonDir();
    const hooks = path.join(common, 'hooks');
    if (path.resolve(git(['rev-parse', '--git-path', 'hooks'])) !== path.resolve(hooks)) {
        fail('Custom hooksPath/hook manager detected. Integrate the validator there explicitly; no hooks or settings were replaced.');
    }
    fs.mkdirSync(policyDir(), { recursive: true });
    fs.mkdirSync(hooks, { recursive: true });
    // Preflight all existing hooks before any config/hook mutation.
    for (const event of events) {
        const hook = path.join(hooks, event);
        if (fs.existsSync(hook) && !fs.readFileSync(hook, 'utf8').includes(marker) && fs.existsSync(hook + '.privacy-original')) {
            fail('Existing hook backup conflict; installation stopped without replacing hooks.');
        }
    }
    const backup = path.join(policyDir(), 'settings-backup.json');
    const keys = ['user.name', 'user.email', 'user.useConfigOnly'];
    if (!fs.existsSync(backup)) {
        const settings = Object.fromEntries(keys.map(key => {
            const result = spawnSync('git', ['config', '--local', '--get-all', key], { encoding: 'utf8' });
            if (result.status !== 0 && result.status !== 1) fail('Settings backup unavailable.');
            return [key, result.status === 0 ? result.stdout.trimEnd().split('\n') : []];
        }));
        fs.writeFileSync(backup, JSON.stringify(settings), { mode: 0o600 });
    }
    const policyFile = path.join(policyDir(), 'policy.json');
    if (identities) {
        // Re-running installation must not discard an explicitly reviewed
        // additional public contributor/bot identity already in local policy.
        const current = fs.existsSync(policyFile) ? JSON.parse(fs.readFileSync(policyFile, 'utf8')).identities : [];
        const retained = Array.isArray(current) ? current : [];
        for (const pair of identities) if (!retained.some(p => p.name === pair.name && p.email === pair.email)) retained.push(pair);
        fs.writeFileSync(policyFile, JSON.stringify({ identities: retained }, null, 2), { mode: 0o600 });
        for (const [key, value] of [['user.name', identities[0].name], ['user.email', identities[0].email], ['user.useConfigOnly', 'true']]) {
            git(['config', '--local', key, value]);
        }
    } else if (!fs.existsSync(policyFile)) {
        fs.writeFileSync(policyFile, JSON.stringify({ identities: [] }), { mode: 0o600 });
    }
    const runtime = path.join(policyDir(), 'guard.mjs');
    if (path.resolve(fileURLToPath(import.meta.url)) !== path.resolve(runtime)) fs.copyFileSync(fileURLToPath(import.meta.url), runtime);
    for (const event of events) {
        const hook = path.join(hooks, event);
        if (fs.existsSync(hook) && !fs.readFileSync(hook, 'utf8').includes(marker)) fs.renameSync(hook, hook + '.privacy-original');
        fs.writeFileSync(hook, `#!/bin/sh\n${marker}\nruntime=${shellQuote(process.execPath.replaceAll('\\', '/'))}\nguard=${shellQuote(runtime.replaceAll('\\', '/'))}\nif [ ! -x "$runtime" ] || [ ! -f "$guard" ]; then\n  echo 'Publication validator unavailable; rerun project setup.' >&2\n  exit 1\nfi\nexec "$runtime" "$guard" ${event} "$@"\n`, { mode: 0o755 });
        fs.chmodSync(hook, 0o755);
    }
    console.log('Project publication hooks installed; existing hooks preserved.');
    if (identities) effective(loadPolicy());
}

try {
    const [mode, ...args] = process.argv.slice(2);
    if (mode === 'install') install(args);
    else {
        const policy = loadPolicy();
        if (mode === 'check' || mode === 'pre-commit') effective(policy);
        else if (mode === 'commit-msg') trailers(fs.readFileSync(args[0], 'utf8'), policy);
        else if (mode === 'pre-push') {
            const input = fs.readFileSync(0, 'utf8');
            push(args[1], input, policy);
            previousHook(mode, args, input);
        } else fail('Unknown publication validator mode.');
        if (mode === 'pre-commit' || mode === 'commit-msg') previousHook(mode, args);
        if (mode === 'check') console.log('Resolved author and committer match approved public policy.');
    }
} catch (error) {
    // Never relay Git stderr, raw identities, config values or filesystem paths.
    const safe = error instanceof SyntaxError || error?.code ? 'Validation unavailable or malformed policy; rerun project setup.' : error.message;
    console.error('Privacy guard: ' + safe);
    process.exitCode = 1;
}
