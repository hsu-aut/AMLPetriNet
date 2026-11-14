/**
 * Takes the picture for the README: the MPS500 quality check with the findings
 * panel open, drawn by the web application.
 *
 * The web application has to be running already, for instance with
 *   dotnet run --project dotnet/PtMapper.Web -c Release --urls http://127.0.0.1:5199
 * which is where the base URL below points.
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
const page = await browser.newPage({ viewport: { width: 1500, height: 700 } });
await page.goto(base + '/');
await page.waitForFunction(() => !document.getElementById('example').disabled, null, { timeout: 20000 });
await page.click('#example');
// The page is done when saving is possible: the net has been drawn by then.
await page.waitForFunction(() => !document.getElementById('aml').disabled, null, { timeout: 20000 });
// The palette covers the left edge of the drawing; move the drawing out from
// under it before the picture is taken.
await page.evaluate(() => window.ptn.get('canvas').scroll({ dx: 70, dy: 0 }));
await page.click('.ptn-findings-button');
await page.waitForSelector('.ptn-finding', { timeout: 20000 });
await page.waitForTimeout(500);
await page.screenshot({ path: out });
await browser.close();
console.log('wrote ' + out);
