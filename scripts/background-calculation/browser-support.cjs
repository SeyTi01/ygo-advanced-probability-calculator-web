// Shared setup for the small published-app smoke set; Playwright stays external.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');

async function serve(directory) {
    const root = path.resolve(directory);
    await fs.access(path.join(root, '_framework', 'dotnet.js'));
    const types = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
        '.html': 'text/html', '.css': 'text/css' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            let file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            if (!file.startsWith(root + path.sep)) return response.writeHead(403).end();
            if (!path.extname(file)) file = path.join(root, 'index.html');
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    return { server, url: `http://127.0.0.1:${server.address().port}` };
}

async function load(page, session) {
    await page.waitForFunction(() => document.querySelector('#sessionFileInput')?._blazorInputFileNextFileId !== undefined);
    await page.locator('#sessionFileInput').setInputFiles({ name: 'smoke.json', mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(session)) });
    await page.waitForFunction(name => {
        try { return JSON.parse(JSON.parse(localStorage.getItem('ygo-calculator:session-recovery:v1')).payload).Cards[0].Name === name; }
        catch { return false; }
    }, session.Cards[0].Name).catch(async error => {
        console.error('Session load did not accept the marker:', await page.getByRole('alert').allTextContents());
        throw error;
    });
}

async function mode(page, value) {
    await page.getByRole('button', { name: 'Calculation settings', exact: true }).click();
    await page.locator('#calculationModel').selectOption(value);
    await page.locator('#calculationModel').press('Escape');
    assert.equal(await page.getByRole('button', { name: 'Calculation settings', exact: true })
        .evaluate(element => element === document.activeElement), true, 'settings restores keyboard focus');
}

async function usable(page, locator) {
    await locator.scrollIntoViewIfNeeded();
    // Playwright's trial click checks visibility, enabled state, and obstruction.
    await locator.click({ trial: true });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true,
        'page must fit the viewport');
}

const launchOptions = () => ({ headless: true, executablePath: process.env.YGO_BROWSER_EXECUTABLE,
    ...(process.env.YGO_BROWSER_CHANNEL ? { channel: process.env.YGO_BROWSER_CHANNEL } : {}) });
module.exports = { serve, load, mode, usable, launchOptions };
