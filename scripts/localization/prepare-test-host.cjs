// An isolated source copy: pseudo locales never enter the production project.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const target = path.join(root, 'bin/localization-test-host');
const marker = path.join(target, '.localization-test-host');
if (fs.existsSync(target)) {
    if (!process.argv.includes('--refresh') || !fs.existsSync(marker)) throw new Error('Test host already exists. Use --refresh only for a marked generated copy.');
    // Verify the resolved absolute deletion target is the one generated folder inside bin.
    if (path.relative(path.join(root, 'bin'), target) !== 'localization-test-host') throw new Error('Unexpected test host path.');
    fs.rmSync(target, { recursive: true });
}
fs.cpSync(path.join(root, 'YGOProbabilityCalculatorBlazor'), target, {
    recursive: true,
    filter: source => !['bin', 'obj'].includes(path.basename(source))
});
fs.writeFileSync(marker, 'Generated pseudo-locale integration host; never publish as production.\n');
fs.writeFileSync(path.join(target, 'wwwroot/localization/locales.json'), JSON.stringify([
    { id: 'en', name: 'English' }, { id: 'eo', name: 'Pseudo Esperanto' }, { id: 'de', name: 'Deutsch' }
]));
for (const locale of ['eo']) {
    fs.copyFileSync(path.join(root, 'YGOProbabilityCalculatorBlazorTest/Localization/Strings.fr.resx'),
        path.join(target, `Localization/Strings.${locale}.resx`));
}
console.log(path.join(target, 'YGOProbabilityCalculatorBlazor.csproj'));
