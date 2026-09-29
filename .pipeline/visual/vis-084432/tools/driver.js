/* Visual acceptance driver (re-check cycle 2): executes a JSON plan of browser steps,
 * saves screenshots + full console capture (NG05105 watch), timings. */
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');

const RUN = path.resolve(__dirname, '..');
const SHOTS = path.join(RUN, 'shots');
const LOGS = path.join(RUN, 'logs');
fs.mkdirSync(SHOTS, { recursive: true });
fs.mkdirSync(LOGS, { recursive: true });

const BASE = 'http://localhost:4300';

async function main() {
  const planPath = process.argv[2];
  const plan = JSON.parse(fs.readFileSync(planPath, 'utf8'));
  const out = { plan: plan.name, steps: [], console: [], pageerrors: [], failedRequests: [], codes: [], ng05105: [] };

  const browser = await puppeteer.launch({
    executablePath: '/snap/bin/chromium',
    headless: 'new',
    args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--lang=ru-RU', '--window-size=1400,1000'],
    defaultViewport: plan.viewport || { width: 1280, height: 900 },
  });
  const page = await browser.newPage();

  page.on('console', (msg) => {
    const t = msg.type();
    const text = msg.text();
    if (t === 'error' || t === 'warning' || text.includes('mock-email')) {
      out.console.push({ type: t, text: text.slice(0, 400) });
      if (/NG05105/.test(text)) out.ng05105.push(text.slice(0, 400));
      const m = text.match(/\[mock-email\][^:]*:\s*(\d{6})/) || (text.includes('mock-email') ? text.match(/(\d{6})/) : null);
      if (text.includes('mock-email') && m) out.codes.push(m[1]);
    }
  });
  page.on('pageerror', (e) => { out.pageerrors.push(String(e).slice(0, 400)); if (/NG05105/.test(String(e))) out.ng05105.push(String(e).slice(0, 400)); });
  page.on('requestfailed', (r) => {
    const u = r.url();
    if (!u.startsWith('data:')) out.failedRequests.push({ url: u.slice(0, 200), err: r.failure() && r.failure().errorText });
  });

  const waitReady = async (ms = 400) => { await new Promise((r) => setTimeout(r, ms)); };

  try {
    for (const [i, step] of (plan.steps || []).entries()) {
      const rec = { i, step: JSON.parse(JSON.stringify(step)) };
      try {
        switch (step.do) {
          case 'viewport':
            await page.setViewport(step.viewport);
            await waitReady(step.wait || 400);
            break;
          case 'goto': {
            const t0 = Date.now();
            await page.goto(BASE + step.url, { waitUntil: step.until || 'networkidle0', timeout: step.timeout || 30000 });
            if (step.waitFor) await page.waitForSelector(step.waitFor, { timeout: step.timeout || 20000 }).catch(() => {});
            await waitReady(step.wait != null ? step.wait : 700);
            rec.ms = Date.now() - t0;
            break;
          }
          case 'reload': {
            const t0 = Date.now();
            await page.reload({ waitUntil: 'networkidle0', timeout: 30000 });
            if (step.waitFor) await page.waitForSelector(step.waitFor, { timeout: step.timeout || 20000 }).catch(() => {});
            await waitReady(step.wait != null ? step.wait : 900);
            rec.ms = Date.now() - t0;
            break;
          }
          case 'waitSelector':
            await page.waitForSelector(step.sel, { timeout: step.timeout || 15000 });
            break;
          case 'waitForFunction':
            await page.waitForFunction(new Function('return (' + step.js + ')'), { timeout: step.timeout || 15000 });
            break;
          case 'wait':
            await new Promise((r) => setTimeout(r, step.ms || 1000));
            break;
          case 'type': {
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.click(step.sel, { clickCount: 3 });
            if (step.clear !== false) await page.keyboard.press('Backspace');
            await page.type(step.sel, step.text, { delay: 10 });
            break;
          }
          case 'click':
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.click(step.sel);
            break;
          case 'clickText': {
            const ok = await page.evaluate(({ sel, text, idx }) => {
              const els = Array.from(document.querySelectorAll(sel));
              const matches = els.filter((e) => (e.textContent || '').trim().includes(text));
              const el = matches[idx || 0];
              if (el) { el.click(); return true; }
              return false;
            }, { sel: step.sel, text: step.text, idx: step.idx });
            if (!ok) throw new Error('clickText not found: ' + step.text);
            break;
          }
          case 'hover':
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.hover(step.sel);
            break;
          case 'focus':
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.focus(step.sel);
            break;
          case 'press':
            await page.keyboard.press(step.key);
            break;
          case 'typePage': {
            // type into focused element via keyboard (for overlay inputs)
            await page.keyboard.type(step.text, { delay: 15 });
            break;
          }
          case 'evalType': {
            // evaluate js -> string, then type it into focused element via keyboard
            const val = await page.evaluate(new Function('return (' + step.js + ')'));
            await page.keyboard.type(String(val), { delay: 25 });
            rec.typed = String(val);
            break;
          }
          case 'focusInput': {
            // focus nth input inside a container matched by js
            await page.evaluate(new Function('return (' + step.js + ')'));
            break;
          }
          case 'eval':
            out[step.as || 'eval_' + i] = await page.evaluate(new Function('return (' + step.js + ')'));
            break;
          case 'localStorage':
            await page.evaluate(([k, v]) => { if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); }, [step.key, step.value]);
            break;
          case 'clearStorage':
            try {
              await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
            } catch (e) { /* about:blank — fresh profile has empty storage anyway */ }
            await waitReady(150);
            break;
          case 'screenshot': {
            const file = path.join(SHOTS, step.name.endsWith('.png') ? step.name : step.name + '.png');
            await page.screenshot({ path: file, fullPage: !!step.fullPage });
            rec.shot = file;
            break;
          }
          case 'textDump':
            rec.text = await page.evaluate(() => document.body.innerText.slice(0, 4000));
            break;
          case 'setValue': {
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.evaluate(([sel, value]) => {
              const el = document.querySelector(sel);
              const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
              const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
              setter.call(el, value);
              el.dispatchEvent(new Event('input', { bubbles: true }));
            }, [step.sel, step.value]);
            break;
          }
          case 'setValueJs': {
            // evaluate js -> string, set into input via native setter + input event
            const v = await page.evaluate(new Function('return (' + step.js + ')'));
            await page.evaluate(([sel, value]) => {
              const el = document.querySelector(sel);
              const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
              setter.call(el, value);
              el.dispatchEvent(new Event('input', { bubbles: true }));
            }, [step.sel, String(v)]);
            rec.set = String(v);
            break;
          }
          case 'scrollTo': {
            await page.evaluate((sel) => { const el = document.querySelector(sel); if (el) el.scrollIntoView({ block: 'center' }); }, step.sel);
            await waitReady(300);
            break;
          }
          case 'blur':
            await page.evaluate(() => document.activeElement && document.activeElement.blur());
            break;
          default:
            throw new Error('unknown step: ' + step.do);
        }
        rec.ok = true;
      } catch (e) {
        rec.ok = false;
        rec.error = String(e).slice(0, 300);
      }
      out.steps.push(rec);
      if (step.abortOnFail && !rec.ok) break;
    }
  } finally {
    await browser.close();
  }
  fs.writeFileSync(path.join(LOGS, plan.name + '.out.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify({
    name: plan.name,
    failed: out.steps.filter((s) => !s.ok).map((s) => ({ i: s.i, do: s.step.do, err: s.error })),
    codes: out.codes,
    ng05105: out.ng05105.length,
    pageerrors: out.pageerrors.length,
    failedRequests: out.failedRequests.length,
  }));
}

main().catch((e) => { console.error('DRIVER-FAIL', e); process.exit(1); });
