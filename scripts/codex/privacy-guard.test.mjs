import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const guard = fileURLToPath(new URL('./privacy-guard.mjs', import.meta.url));
const publicName = 'ExampleOwner';
const publicEmail = 'owner@example.invalid';
const wrongName = 'Fictional Private Person';
const wrongEmail = 'private@example.invalid';
const environment = { ...process.env };
for (const key of Object.keys(environment)) {
    if (/^GIT_(AUTHOR|COMMITTER|CONFIG|DIR$|WORK_TREE$|INDEX_FILE$)|^EMAIL$/.test(key)) delete environment[key];
}
environment.GIT_TERMINAL_PROMPT = '0';
function run(repo, program, args, options = {}) {
    return spawnSync(program, args, { cwd: repo, encoding: 'utf8', env: { ...environment, ...options.env }, input: options.input });
}
function git(repo, args, options) { return run(repo, 'git', args, options); }
function success(result) {
    // Failed diagnostics can include a private test-directory path: keep output
    // redacted even when an assertion fails.
    assert.equal(result.status, 0, 'Expected command to succeed (diagnostics redacted).');
    return result.stdout.trim();
}
function rejected(result) {
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /Privacy guard:/);
    assert.equal(result.stderr.includes(wrongName), false);
    assert.equal(result.stderr.includes(wrongEmail), false);
}
function install(repo, args = ['--name', publicName, '--email', publicEmail]) {
    return run(repo, process.execPath, [guard, 'install', ...args]);
}
function stage(repo, text = 'example') {
    fs.writeFileSync(path.join(repo, 'example.txt'), text);
    success(git(repo, ['add', 'example.txt']));
}
function commit(repo, message = 'Example change', options) {
    stage(repo, message);
    return git(repo, ['commit', '-m', message], options);
}
function fixture(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ygo-privacy-test-'));
    const repo = path.join(root, 'checkout with spaces');
    const remote = path.join(root, 'remote.git');
    success(run(root, 'git', ['init', '-b', 'main', repo]));
    success(run(root, 'git', ['init', '--bare', remote]));
    success(git(repo, ['remote', 'add', 'origin', remote]));
    t.after(() => {
        assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir()));
        assert.match(path.basename(root), /^ygo-privacy-test-/);
        fs.rmSync(root, { recursive: true, force: true });
    });
    return { root, repo, remote };
}
function rawCommit(repo, parent, { author = publicName, authorEmail = publicEmail, committer = publicName, committerEmail = publicEmail, message = 'Example raw commit' } = {}) {
    const tree = success(git(repo, ['write-tree']));
    return success(git(repo, ['commit-tree', tree, ...(parent ? ['-p', parent] : [])], {
        input: message + '\n', env: { GIT_AUTHOR_NAME: author, GIT_AUTHOR_EMAIL: authorEmail, GIT_COMMITTER_NAME: committer, GIT_COMMITTER_EMAIL: committerEmail }
    }));
}

test('ordinary commits use installed defaults in fresh processes and a new workspace', t => {
    const { repo, root } = fixture(t);
    success(install(repo));
    success(commit(repo));
    const raw = success(git(repo, ['cat-file', 'commit', 'HEAD']));
    for (const role of ['author', 'committer']) assert.match(raw, new RegExp(`^${role} ${publicName} <${publicEmail}>`, 'm'));
    success(run(repo, process.execPath, [guard, 'check']));
    const fresh = path.join(root, 'fresh workspace');
    success(run(root, 'git', ['clone', repo, fresh]));
    success(install(fresh, []));
    rejected(commit(fresh));
    success(install(fresh));
    success(commit(fresh, 'Fresh workspace example'));
    assert.match(success(git(fresh, ['cat-file', 'commit', 'HEAD'])), new RegExp(`^author ${publicName} <${publicEmail}>`, 'm'));
});

test('real pre-commit rejects author/committer environment overrides and wrong name', t => {
    const { repo } = fixture(t);
    success(install(repo));
    for (const env of [{ GIT_AUTHOR_EMAIL: wrongEmail }, { GIT_COMMITTER_EMAIL: wrongEmail }, { GIT_AUTHOR_NAME: wrongName }]) rejected(commit(repo, 'Example override', { env }));
    stage(repo);
    rejected(git(repo, ['-c', 'user.name=' + wrongName, 'commit', '-m', 'Example command override']));
    success(commit(repo, 'Approved default'));
});

test('reused and cherry-picked authors cannot hide behind safe committer defaults', t => {
    const { repo } = fixture(t);
    success(install(repo));
    success(commit(repo));
    const parent = success(git(repo, ['rev-parse', 'HEAD']));
    stage(repo, 'Reused example');
    const bad = rawCommit(repo, parent, { authorEmail: wrongEmail });
    rejected(git(repo, ['commit', '-C', bad]));
    success(git(repo, ['restore', '--staged', 'example.txt']));
    success(git(repo, ['restore', 'example.txt']));
    const picked = git(repo, ['cherry-pick', bad]);
    // Git cherry-pick may not invoke pre-commit: the outgoing-object hook is the
    // mandatory second barrier. Either rejection point is acceptable.
    if (picked.status === 0) rejected(git(repo, ['push', 'origin', 'HEAD:refs/heads/picked']));
    else assert.notEqual(picked.status, 0);
});

test('identity trailers are rejected by commit-msg and outgoing-object inspection', t => {
    const { repo } = fixture(t);
    success(install(repo));
    for (const trailer of ['Signed-off-by', 'Co-authored-by']) rejected(commit(repo, `Example trailer\n\n${trailer}: ${wrongName} <${wrongEmail}>`));
    success(commit(repo, `Approved trailer\n\nSigned-off-by: ${publicName} <${publicEmail}>`));
    const parent = success(git(repo, ['rev-parse', 'HEAD']));
    const bad = rawCommit(repo, parent, { message: `Example raw trailer\n\nCo-authored-by: ${wrongName} <${wrongEmail}>` });
    success(git(repo, ['update-ref', 'refs/heads/raw-trailer', bad]));
    rejected(git(repo, ['push', 'origin', 'raw-trailer']));
});

test('first push checks intermediate author, committer and name even with a clean tip', t => {
    for (const metadata of [{ authorEmail: wrongEmail }, { committerEmail: wrongEmail }, { author: wrongName }]) {
        const { repo, remote } = fixture(t);
        success(install(repo));
        success(commit(repo));
        const first = success(git(repo, ['rev-parse', 'HEAD']));
        const bad = rawCommit(repo, first, metadata);
        const tip = rawCommit(repo, bad);
        success(git(repo, ['update-ref', 'refs/heads/outgoing', tip]));
        rejected(git(repo, ['push', 'origin', 'outgoing']));
        assert.notEqual(git(remote, ['rev-parse', '--verify', 'refs/heads/outgoing']).status, 0);
    }
});

test('new branch excludes only actual published ancestry, not stale tracking refs', t => {
    const { repo, remote } = fixture(t);
    success(install(repo));
    stage(repo);
    const published = rawCommit(repo, null, { authorEmail: wrongEmail });
    success(git(repo, ['update-ref', 'refs/heads/main', published]));
    // Seed only this disposable local remote before testing newly outgoing work.
    success(git(remote, ['fetch', repo, 'main:refs/heads/main']));
    success(commit(repo, 'New public change'));
    success(git(repo, ['push', 'origin', 'HEAD:refs/heads/clean-new']));
    const current = success(git(repo, ['rev-parse', 'HEAD']));
    const bad = rawCommit(repo, current, { committerEmail: wrongEmail });
    success(git(repo, ['update-ref', 'refs/heads/bad', bad]));
    success(git(repo, ['update-ref', 'refs/remotes/origin/stale', bad]));
    rejected(git(repo, ['push', 'origin', 'bad']));
});

test('all ref updates, annotated taggers/trailers and deletions are handled', t => {
    const { repo } = fixture(t);
    success(install(repo));
    success(commit(repo));
    const current = success(git(repo, ['rev-parse', 'HEAD']));
    success(git(repo, ['branch', 'clean', current]));
    const bad = rawCommit(repo, current, { authorEmail: wrongEmail });
    success(git(repo, ['update-ref', 'refs/heads/bad', bad]));
    rejected(git(repo, ['push', 'origin', 'clean', 'bad']));
    success(git(repo, ['tag', '-a', 'bad-tagger', '-m', 'Example tag'], { env: { GIT_COMMITTER_EMAIL: wrongEmail } }));
    rejected(git(repo, ['push', 'origin', 'refs/tags/bad-tagger']));
    success(git(repo, ['tag', '-a', 'bad-trailer', '-m', `Example tag\n\nCo-authored-by: ${wrongName} <${wrongEmail}>`]));
    rejected(git(repo, ['push', 'origin', 'refs/tags/bad-trailer']));
    success(git(repo, ['tag', '-a', 'public-tag', '-m', 'Approved example tag']));
    success(git(repo, ['tag', 'lightweight']));
    success(git(repo, ['push', 'origin', 'clean', 'refs/tags/public-tag', 'refs/tags/lightweight']));
    success(git(repo, ['push', 'origin', ':refs/heads/clean']));
});

test('replacement objects and grafts cannot conceal outgoing metadata', t => {
    const { repo } = fixture(t);
    success(install(repo));
    stage(repo);
    const outgoing = rawCommit(repo, null, { authorEmail: wrongEmail });
    const replacement = rawCommit(repo, null);
    success(git(repo, ['update-ref', 'refs/heads/main', outgoing]));
    success(git(repo, ['replace', outgoing, replacement]));
    rejected(git(repo, ['push', 'origin', 'main']));
    success(git(repo, ['replace', '-d', outgoing]));
    fs.mkdirSync(path.join(repo, '.git', 'info'), { recursive: true });
    fs.writeFileSync(path.join(repo, '.git', 'info', 'grafts'), outgoing + '\n');
    rejected(git(repo, ['push', 'origin', 'main']));
});

test('existing hook arguments, stdin and failures survive idempotent installation', t => {
    const { repo } = fixture(t);
    const hooks = path.join(repo, '.git', 'hooks');
    const original = '#!/bin/sh\nprintf "%s\\n" "$@" > .git/original-args\ncat > .git/original-input\n';
    fs.writeFileSync(path.join(hooks, 'pre-push'), original, { mode: 0o755 });
    fs.writeFileSync(path.join(hooks, 'pre-commit'), '#!/bin/sh\necho survived > .git/original-commit\n', { mode: 0o755 });
    fs.writeFileSync(path.join(hooks, 'commit-msg'), '#!/bin/sh\ntest -s "$1"\n', { mode: 0o755 });
    success(install(repo));
    success(install(repo));
    assert.equal(fs.readFileSync(path.join(hooks, 'pre-push.privacy-original'), 'utf8'), original);
    success(commit(repo));
    success(git(repo, ['push', 'origin', 'HEAD:refs/heads/kept']));
    assert.equal(fs.readFileSync(path.join(repo, '.git', 'original-commit'), 'utf8').trim(), 'survived');
    assert.equal(fs.readFileSync(path.join(repo, '.git', 'original-args'), 'utf8').split('\n')[0], 'origin');
    assert.match(fs.readFileSync(path.join(repo, '.git', 'original-input'), 'utf8'), /HEAD [0-9a-f]+ refs\/heads\/kept 0+/);
    fs.writeFileSync(path.join(hooks, 'pre-push.privacy-original'), '#!/bin/sh\nexit 7\n');
    assert.equal(git(repo, ['push', 'origin', 'HEAD:refs/heads/stopped']).status, 1);
});

test('custom hook manager is untouched; missing policy/runtime fails closed', t => {
    const { repo, root } = fixture(t);
    const manager = path.join(root, 'existing-manager');
    fs.mkdirSync(manager);
    success(git(repo, ['config', 'core.hooksPath', manager]));
    rejected(install(repo));
    assert.equal(success(git(repo, ['config', 'core.hooksPath'])), manager);
    success(git(repo, ['config', '--unset', 'core.hooksPath']));
    success(install(repo, []));
    rejected(commit(repo));
    success(install(repo));
    fs.writeFileSync(path.join(repo, '.git', 'privacy-guard', 'policy.json'), '{');
    rejected(commit(repo));
    fs.unlinkSync(path.join(repo, '.git', 'privacy-guard', 'guard.mjs'));
    assert.notEqual(commit(repo).status, 0);
});

test('shallow and incomplete history block publication', t => {
    const { root, repo, remote } = fixture(t);
    success(install(repo));
    success(commit(repo));
    success(commit(repo, 'Second example'));
    success(git(repo, ['push', 'origin', 'main']));
    const shallow = path.join(root, 'shallow');
    success(run(root, 'git', ['clone', '--depth=1', 'file://' + remote.replaceAll('\\', '/'), shallow]));
    success(install(shallow));
    success(commit(shallow));
    rejected(git(shallow, ['push', 'origin', 'HEAD:refs/heads/shallow']));
    // A server branch unknown to a different complete client is not silently
    // omitted from the publication boundary.
    const second = path.join(root, 'second');
    success(run(root, 'git', ['clone', remote, second]));
    success(install(second));
    success(commit(repo, 'Server branch'));
    success(git(repo, ['push', 'origin', 'HEAD:refs/heads/new-server']));
    success(commit(second));
    rejected(git(second, ['push', 'origin', 'HEAD:refs/heads/incomplete']));
});
