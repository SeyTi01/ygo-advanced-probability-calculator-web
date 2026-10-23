const assert = require('node:assert/strict');

// Reuse the optimized-app operator harness, including real catalog selection,
// session download/upload, responsive checks, themes and screenshots.
module.exports = async ({ page, load, setLayout, saveSession, capture, assertNoOverflow, session }) => {
    const presets = [
        [32807846, 'Reinforcement of the Army', 4, 2],
        [85106525, 'Bonfire', 4, 2],
        [57103969, 'Fire Formation - Tenki', 4, 2],
        [72323266, 'Earthbound Prisoner Stone Sweeper', 3, 3],
        [96729612, 'Preparation of Rites', 7, 2],
        [38120068, 'Trade-In', 8, 1],
        [92221402, 'Chaotic Elements'],
        [67115133, 'Radiant Typhoon Chant'],
        [48805472, 'Traptantalizing Tune']
    ];
    const fixture = { ...session(), Cards: [], Categories: [], Combos: [{
        Name: 'Operator review combo', Active: true, Categories: [], Cards: [], AlternativeGroups: []
    }] };
    await load(fixture);
    const quick = page.getByRole('combobox', { name: 'Add card by name' });
    for (const [, name] of presets) {
        await quick.fill(name);
        await page.getByRole('option', { name, exact: true }).click();
    }
    const imported = await saveSession();
    const nodes = selector => [selector, ...(selector.Children || []).flatMap(nodes)];
    const selectorFor = card => card.Search?.Selector || card.PreDrawSelector;
    for (const [id, name, maximum] of presets.filter(preset => preset.length > 2)) {
        const card = imported.Cards.find(card => card.ExternalCardId === id);
        const selector = selectorFor(card);
        const numeric = nodes(selector).filter(node => node.Kind === 'NumericRange');
        assert.equal(numeric.length, 1, `${name}: one serialized numeric predicate after actual catalog import`);
        assert.equal(numeric[0].Property, 'Level');
        assert.equal(numeric[0].Minimum, id === 38120068 ? 8 : 1);
        assert.equal(numeric[0].Maximum, maximum);
        assert.equal(nodes(selector).some(node => node.Categories?.some(identity => identity.startsWith('metadata:level:'))), false);
    }
    const chant = imported.Cards.find(card => card.ExternalCardId === 67115133);
    assert.deepEqual(chant.Search.Modes.map(mode => mode.ActivationLimit), [1, 1]);
    assert.equal(nodes(chant.Search.Modes[0].Selector).filter(node => node.Kind === 'NumericRange').length, 1);
    const tune = imported.Cards.find(card => card.ExternalCardId === 48805472);
    assert.equal(tune.PreDrawSelector.Kind, 'Objective');
    assert.equal(nodes(tune.PreDrawSelector).filter(node => node.Kind === 'NumericRange').length, 1);
    const cardRow = name => page.locator('.card-editor').filter({ has: page.locator('.card-header-name', { hasText: name }) });
    const open = async row => {
        if (await row.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await row.locator('.accordion-button').click(); }
        await row.locator('.simulation-edit').click();
    };
    for (const width of [1280, 768, 390, 320]) {
        for (const theme of ['light', 'dark']) {
            await setLayout(width, theme);
            for (const [id, name, maximum, operands] of presets) {
                const row = cardRow(name);
                await open(row);
                if (maximum) {
                    assert.equal(await row.locator('.selector-expression .requirement-row').count(), 1, `${name}/${width}/${theme}: one field`);
                    assert.equal(await row.locator('.selector-expression .draw-selector-chip').count(), operands);
                    await row.locator('.selector-expression .requirement-edit').click();
                    assert.equal(await row.locator('.requirement-operand').count(), operands);
                    const form = row.locator('.selector-expression form');
                    assert.equal(await form.getByLabel('Comparison', { exact: true }).inputValue(), id === 38120068 ? 'exact' : 'range');
                    await assertNoOverflow(`${name} edit ${width}/${theme}`);
                    await form.getByLabel(id === 38120068 ? 'Level' : 'Minimum Level', { exact: true }).fill('2');
                    await form.press('Escape');
                    await row.locator('.selector-expression .requirement-edit').click();
                    assert.equal(await form.getByLabel(id === 38120068 ? 'Level' : 'Minimum Level', { exact: true }).inputValue(), id === 38120068 ? '8' : '1');
                    await form.getByRole('button', { name: 'Save', exact: true }).click();
                    assert.equal(await row.locator('.selector-expression .requirement-row').count(), 1);
                }
                else if (id === 92221402) {
                    assert.equal(await row.locator('.selector-expression .requirement-row').count(), 3, 'meaningful nested OR groups remain separate');
                }
                else {
                    assert.equal(await row.locator('.selector-expression .requirement-edit').count(), 0, 'verified/mode predicates remain protected');
                    if (id === 67115133) {
                        assert.match(await row.locator('.simulation-editor').innerText(), /Level 1–4/);
                        assert.match(await row.locator('.simulation-editor').innerText(), /verified catalog-id:5318639/);
                        assert.match(await row.locator('.simulation-editor').innerText(), /1 per entry/);
                    }
                    else { assert.equal(await row.getByRole('button', { name: 'Replace verified filter', exact: true }).count(), 1); }
                }
                await assertNoOverflow(`${name} ${width}/${theme}`);
                if ((width === 1280 && theme === 'light' || width === 390 && theme === 'dark') && [32807846, 72323266, 92221402, 67115133].includes(id)) {
                    await capture(`canonical-${id}-${width}-${theme}`);
                }
                await row.locator('.simulation-editor').getByRole('button', { name: 'Cancel', exact: true }).click();
                await row.locator('.accordion-button').click();
            }
        }
    }
    const unchanged = await saveSession();
    assert.deepEqual(unchanged.Cards.map(card => [selectorFor(card), card.EffectProfile]), imported.Cards.map(card => [selectorFor(card), card.EffectProfile]));
    // Save a real numeric change, download/reload it, then explicitly restore.
    await setLayout(1280, 'light');
    const rota = cardRow('Reinforcement of the Army');
    await open(rota);
    await rota.locator('.selector-expression .requirement-edit').click();
    await rota.getByLabel('Minimum Level', { exact: true }).fill('2');
    await rota.locator('.selector-expression form').getByRole('button', { name: 'Save', exact: true }).click();
    await rota.locator('.simulation-save').click();
    const edited = await saveSession();
    const editedRota = edited.Cards.find(card => card.ExternalCardId === 32807846);
    assert.equal(editedRota.EffectProfile.Source, 'Custom');
    assert.equal(nodes(editedRota.Search.Selector).find(node => node.Kind === 'NumericRange').Minimum, 2);
    await load(edited);
    if (await rota.locator('.accordion-button').getAttribute('aria-expanded') !== 'true') { await rota.locator('.accordion-button').click(); }
    await rota.getByRole('button', { name: 'Restore built-in simulation', exact: true }).click();
    const restored = await saveSession();
    assert.deepEqual(restored.Cards.find(card => card.ExternalCardId === 32807846).Search, imported.Cards.find(card => card.ExternalCardId === 32807846).Search);
    // Loading and unchanged editing of a shipped literal snapshot retain its semantics.
    const legacy = structuredClone(imported);
    const legacyRota = legacy.Cards.find(card => card.ExternalCardId === 32807846);
    legacyRota.EffectProfile.Revision = 3;
    legacyRota.Search.Selector = { Kind: 'AllOf', Children: [
        { Kind: 'Category', Categories: ['metadata:monster-race:warrior'] },
        { Kind: 'AnyOf', Categories: [1, 2, 3, 4].map(level => `metadata:level:${level}`) }
    ] };
    await load(legacy);
    await open(rota);
    assert.equal(await rota.locator('.selector-expression .requirement-row').count(), 2);
    for (const field of await rota.locator('.selector-expression .requirement-row').all()) {
        await field.locator('.requirement-edit').click();
        await field.locator('form').getByRole('button', { name: 'Save', exact: true }).click();
    }
    await capture('canonical-legacy-rota-1280-light');
    await rota.locator('.simulation-save').click();
    const legacySaved = (await saveSession()).Cards.find(card => card.ExternalCardId === 32807846);
    assert.deepEqual(legacySaved.Search, legacyRota.Search);
    assert.deepEqual(legacySaved.EffectProfile, legacyRota.EffectProfile);
    console.log('PASS canonical presets: genuine imported/restored numeric plans, one-field range editing, Cancel/Save/load, nested/protected modes, legacy literal preservation; desktop/tablet/mobile/320px in light and dark.');
};
