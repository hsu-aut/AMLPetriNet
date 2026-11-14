/**
 * Takes the picture for the README: the MPS500 quality check with the findings
 * panel open, drawn by the web application.
 *
 * Usage: node tools/screenshot.mjs [baseUrl] [out.png]
 */
import { chromium } from 'playwright';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const base = process.argv[2] ?? 'http://127.0.0.1:5199';
const out = resolve(process.argv[3] ?? resolve(here, '../../docs/screenshot.png'));

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1500, height: 620 } });
await page.goto(base + '/');
await page.waitForFunction(() => !document.getElementById('example').disabled, null, { timeout: 20000 });
await page.click('#example');
// The page is done when saving is possible: the net has been drawn by then.
await page.waitForFunction(() => !document.getElementById('aml').disabled, null, { timeout: 20000 });
await page.click('.ptn-findings-button');
await page.waitForSelector('.ptn-finding', { timeout: 20000 });
await page.waitForTimeout(500);
await page.screenshot({ path: out });
await browser.close();
console.log('wrote ' + out);
