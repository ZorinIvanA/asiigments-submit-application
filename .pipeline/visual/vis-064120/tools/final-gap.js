const puppeteer = require('puppeteer-core');
(async () => {
  const b = await puppeteer.launch({ executablePath: '/snap/bin/chromium', headless: 'new', args: ['--no-sandbox','--disable-dev-shm-usage'], defaultViewport: { width: 767, height: 900 } });
  const p = await b.newPage();
  // VS-009: recovery email step mobile with error
  await p.goto('http://localhost:4300/recovery', { waitUntil: 'networkidle2' });
  await p.click('#email-input'); await p.type('#email-input', 'not-an-email');
  const btnInfo = await p.evaluate(() => { const btns=[...document.querySelectorAll('button')]; return btns.map(x=>({t:x.innerText.trim(), d:x.disabled})); });
  const submit = await p.evaluate(() => { const btns=[...document.querySelectorAll('button')]; const s=btns.find(x=>x.innerText.trim()==='Отправить код'); if (s && !s.disabled) { s.click(); return 'clicked'; } return 'disabled'; });
  await new Promise(r => setTimeout(r, 700));
  await p.screenshot({ path: '../shots/VS009-recovery-767-error-resubmit.png' });
  const errText = await p.evaluate(() => document.body.innerText.includes('Введите корректный email'));
  console.log('VS009 buttons:', JSON.stringify(btnInfo), 'submit:', submit, 'errorVisible:', errText);
  // teacher mobile groups/access
  await p.goto('http://localhost:4300/login', { waitUntil: 'networkidle2' });
  await p.click('#login-input'); await p.type('#login-input', 'teacher');
  await p.click('#password-input'); await p.type('#password-input', 'teacher123!');
  await p.click('button[type=submit]'); await new Promise(r => setTimeout(r, 1600));
  await p.goto('http://localhost:4300/groups', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 1500));
  await p.screenshot({ path: '../shots/TS384-groups-767.png' });
  const gOv = await p.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }));
  console.log('groups ov:', JSON.stringify(gOv));
  await p.goto('http://localhost:4300/access', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 1800));
  await p.screenshot({ path: '../shots/TS384-access-767.png' });
  const aOv = await p.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }));
  console.log('access ov:', JSON.stringify(aOv));
  // chain button tooltip (desktop lab form)
  await p.setViewport({ width: 1280, height: 900 });
  await p.goto('http://localhost:4300/works/new', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 900));
  const chain = await p.evaluate(() => { const host = [...document.querySelectorAll('p-button')].find(x => x.querySelector('.pi-link') || x.querySelector('[class*=link]')); const btn = host ? host.querySelector('button') : null; return host ? { title: btn ? btn.title : null, aria: btn ? btn.getAttribute('aria-label') : null, hostTitle: host.getAttribute('title') } : 'no-chain-btn'; });
  console.log('chain:', JSON.stringify(chain));
  await b.close();
})().catch(e => { console.error('FAIL', String(e).slice(0, 300)); process.exit(1); });
