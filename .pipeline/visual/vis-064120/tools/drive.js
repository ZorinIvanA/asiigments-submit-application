/* Visual scenario driver: executes a JSON plan of browser steps, saves screenshots + console log. */
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');

const RUN = path.resolve(__dirname, '..');
const SHOTS = path.join(RUN, 'shots');
fs.mkdirSync(SHOTS, { recursive: true });

async function main() {
  const planPath = process.argv[2];
  const plan = JSON.parse(fs.readFileSync(planPath, 'utf8'));
  const out = { plan: plan.name, steps: [], console: [], pageerrors: [], failedRequests: [], codes: [] };

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
    if (t === 'error' || text.includes('mock-email') || t === 'warning') {
      out.console.push({ type: t, text: text.slice(0, 500) });
      const m = text.match(/\[mock-email\][^:]*:\s*(\d{6})/) || text.match(/(\d{6})/);
      if (text.includes('mock-email') && m) out.codes.push(m[1]);
    }
  });
  page.on('pageerror', (e) => out.pageerrors.push(String(e).slice(0, 500)));
  page.on('requestfailed', (r) => {
    const u = r.url();
    if (!u.startsWith('data:')) out.failedRequests.push({ url: u.slice(0, 200), err: r.failure()?.errorText });
  });

  const waitReady = async (ms = 400) => { await new Promise((r) => setTimeout(r, ms)); };

  try {
    for (const [i, step] of (plan.steps || []).entries()) {
      const rec = { i, step: JSON.parse(JSON.stringify(step)) };
      try {
        switch (step.do) {
          case 'viewport':
            await page.setViewport(step.viewport);
            await waitReady(step.wait || 300);
            break;
          case 'goto':
            await page.goto('http://localhost:4300' + step.url, { waitUntil: 'networkidle0', timeout: 30000 });
            await waitReady(step.wait || 600);
            break;
          case 'reload':
            await page.reload({ waitUntil: 'networkidle0', timeout: 30000 });
            await waitReady(step.wait || 600);
            break;
          case 'waitSelector':
            await page.waitForSelector(step.sel, { timeout: step.timeout || 15000 });
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
            // click first element matching selector whose text includes step.text
            const ok = await page.evaluate(({ sel, text }) => {
              const els = Array.from(document.querySelectorAll(sel));
              const el = els.find((e) => (e.textContent || '').trim().includes(text));
              if (el) { el.click(); return true; }
              return false;
            }, { sel: step.sel, text: step.text });
            if (!ok) throw new Error('clickText not found: ' + step.text);
            break;
          }
          case 'press':
            await page.keyboard.press(step.key);
            break;
          case 'eval':
            out[step.as || 'eval_' + i] = await page.evaluate(new Function('return (' + step.js + ')')());
            break;
          case 'localStorage':
            await page.evaluate(([k, v]) => { if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); }, [step.key, step.value]);
            break;
          case 'clearStorage':
            await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
            break;
          case 'screenshot': {
            const file = path.join(SHOTS, step.name.endsWith('.png') ? step.name : step.name + '.png');
            await page.screenshot({ path: file, fullPage: !!step.fullPage });
            rec.shot = file;
            break;
          }
          case 'textDump': {
            rec.text = await page.evaluate(() => document.body.innerText.slice(0, 3000));
            break;
          }
          case 'blur':
            await page.evaluate(() => document.activeElement && document.activeElement.blur());
            break;
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
  fs.writeFileSync(path.join(RUN, 'logs', plan.name + '.out.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify({ name: plan.name, failed: out.steps.filter((s) => !s.ok).map((s) => ({ i: s.i, err: s.error })), codes: out.codes, pageerrors: out.pageerrors.length, failedRequests: out.failedRequests }));
}

main().catch((e) => { console.error('DRIVER-FAIL', e); process.exit(1); });
