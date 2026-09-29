/* Visual acceptance runner: plans for VS-001..011, TS-229..233, TS-381..388. */
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');

const RUN = path.resolve(__dirname, '..');
const SHOTS = path.join(RUN, 'shots');
const LOGS = path.join(RUN, 'logs');
fs.mkdirSync(SHOTS, { recursive: true });
fs.mkdirSync(LOGS, { recursive: true });

const DESK = { width: 1280, height: 900 };
const MOB = { width: 767, height: 900 };
const EDGE = { width: 768, height: 900 };

const wait = (ms) => ({ do: 'wait', ms });
const shot = (name, fullPage) => ({ do: 'screenshot', name, fullPage: !!fullPage });
const goto = (url, w) => ({ do: 'goto', url, wait: 700 });
const vp = (viewport) => ({ do: 'viewport', viewport, wait: 400 });
const type = (sel, text, extra) => ({ do: 'type', sel, text, ...(extra || {}) });
const click = (sel) => ({ do: 'click', sel });
const clickText = (sel, text) => ({ do: 'clickText', sel, text });
const evaljs = (js, as) => ({ do: 'eval', js, as });
const dump = (as) => ({ do: 'textDump', as: as || 'text' });
const reload = () => ({ do: 'reload', wait: 900 });

// login as user at current viewport
const login = (u, p) => [
  goto('/login'),
  type('#login-input', u),
  type('#password-input', p),
  click('button[type=submit]'),
  wait(1600),
];
const tamperLabId = (idx, bad) => evaljs(
  `(() => { const db = JSON.parse(localStorage.getItem('mock.db.v1')); ` +
  `if (!db.labs[${idx}]) return 'no-lab'; db.labs[${idx}].id = '${bad}'; ` +
  `localStorage.setItem('mock.db.v1', JSON.stringify(db)); return 'tampered-' + ${idx}; })()`,
  'tamper'
);
const removeLabByNumber = (sem, num) => evaljs(
  `(() => { const db = JSON.parse(localStorage.getItem('mock.db.v1')); ` +
  `const before = db.labs.length; db.labs = db.labs.filter(l => !(l.semester === ${sem} && l.number === ${num})); ` +
  `localStorage.setItem('mock.db.v1', JSON.stringify(db)); return 'removed:' + (before - db.labs.length); })()`,
  'tamper'
);
const toastState = (as) => evaljs(
  `(() => { const t = document.querySelector('.p-toast-message'); if (!t) return { present: false }; ` +
  `const r = t.getBoundingClientRect(); return { present: true, x: r.x, y: r.y, text: t.innerText.slice(0, 160), cls: t.className.slice(0, 120) }; })()`,
  as
);
const setRecoveryToken = (token) => evaljs(
  `(() => { sessionStorage.setItem('recovery.flow.v1', JSON.stringify({ email: 'student01@example.com', resetToken: '${token}' })); return 'set'; })()`,
  'storeSet'
);
const overflowInfo = (as) => evaljs(
  `(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }))()`,
  as
);
const delRowButton = (numCell, semCell) => evaljs(
  `(() => { const rows = [...document.querySelectorAll('.works-page__list tbody tr')]; ` +
  `const row = rows.find(r => r.cells.length > 2 && r.cells[0].innerText.trim() === '${numCell}' && r.cells[2].innerText.trim() === '${semCell}'); ` +
  `if (!row) return 'row-not-found'; const btns = row.querySelectorAll('.works-page__actions button'); ` +
  `btns[btns.length - 1].click(); return 'clicked'; })()`,
  'delClick'
);
const dateCell = (rowIdx, cellIdx) => evaljs(
  `(() => { const rows = [...document.querySelectorAll('.submissions-page__card tbody tr')]; ` +
  `const row = rows[${rowIdx}]; if (!row) return 'no-row'; const td = row.cells[${cellIdx}]; ` +
  `if (!td) return 'no-cell'; const inp = td.querySelector('input'); if (!inp) return 'no-input'; ` +
  `inp.focus(); inp.click(); return 'focused'; })()`,
  'cellFocus'
);
const pickDay = (day) => evaljs(
  `(() => { const tds = [...document.querySelectorAll('.p-datepicker-panel td, .p-datepicker td, .p-datepicker-panel span')]; ` +
  `const el = tds.find(td => td.innerText.trim() === '${day}' && !String(td.className).includes('other')); ` +
  `if (!el) return 'day-not-found'; el.click(); return 'picked'; })()`,
  'dayPick'
);

const PLANS = {};

/* ---------- Batch 1: auth + profile ---------- */

PLANS.p01_vs001_vs007 = { name: 'p01_vs001_vs007', viewport: DESK, steps: [
  goto('/login'), shot('VS001-login-desktop-initial'),
  type('#login-input', 'student01'), type('#password-input', 'wrongpass1'),
  click('button[type=submit]'), wait(1600),
  shot('VS001-login-desktop-error'), dump('d1'), toastState('toast1'),
  evaljs(`(() => { const b = document.querySelector('.p-toast button'); if (!b) return 'no-close-btn'; b.click(); return 'closed'; })()`, 'closeClick'),
  wait(400), shot('VS001-login-desktop-error-closed'), toastState('toast2'),
  vp(MOB), goto('/login'),
  type('#login-input', 'student01'), type('#password-input', 'wrongpass2'),
  click('button[type=submit]'), wait(1600),
  shot('VS007-login-mobile-error'), dump('d2'),
  evaljs(`(() => { const form = document.querySelector('form.auth-card'); const fr = form.getBoundingClientRect(); ` +
    `const b = document.querySelector('.app-notification-banner'); const br = b ? b.getBoundingClientRect() : null; ` +
    `return { formY: fr.y, bannerInPage: !!b, bannerY: br ? br.y : null, toastPresent: !!document.querySelector('.p-toast-message') }; })()`, 'mobileLayout'),
  vp(DESK), goto('/login'),
  type('#login-input', 'student01'), type('#password-input', 'wrongpass3'),
  click('button[type=submit]'), wait(1600),
  shot('VS007-login-desktop-error-banner'), toastState('toast3'),
] };

PLANS.p02_vs002_vs008 = { name: 'p02_vs002_vs008', viewport: DESK, steps: [
  goto('/register'), shot('VS002-register-desktop-initial'),
  // empty form: touch fields then blur to surface field errors; submit button may be disabled
  click('#full-name-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b1'),
  click('#login-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b2'),
  click('#email-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b3'),
  click('#password-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b4'),
  click('#repeat-password-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b5'),
  click('button[type=submit]'), wait(600),
  shot('VS002-register-desktop-empty-errors'), dump('d1'),
  // invalid email + weak password
  type('#full-name-input', 'Тестов Тест Тестович'), type('#login-input', 'vsreguser1'),
  type('#email-input', 'broken-email'), type('#password-input', '123'), type('#repeat-password-input', '123'),
  click('button[type=submit]'), wait(600),
  shot('VS002-register-desktop-invalid'), dump('d2'),
  // duplicate login -> 409
  type('#full-name-input', 'Тестов Тест Тестович'), type('#login-input', 'student01'),
  type('#email-input', 'regtest1@example.com'), type('#password-input', 'Validpass1!'), type('#repeat-password-input', 'Validpass1!'),
  click('button[type=submit]'), wait(1800),
  shot('VS002-register-409-login'), dump('d3'), toastState('t1'),
  // duplicate email -> 409
  type('#login-input', 'vsreguser2'), type('#email-input', 'teacher@example.com'),
  click('button[type=submit]'), wait(1800),
  shot('VS002-register-409-email'), dump('d4'), toastState('t2'),
  // mobile
  vp(MOB), goto('/register'), shot('VS008-register-767-initial'), overflowInfo('ov1'),
  click('#full-name-input'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b6'),
  click('#email-input'), type('#email-input', 'bad'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b7'),
  click('#password-input'), type('#password-input', '12'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b8'),
  wait(400), shot('VS008-register-767-errors'), overflowInfo('ov2'),
  type('#full-name-input', 'Мобильный Тест'), type('#login-input', 'vsregm1'),
  type('#repeat-password-input', '34'),
  shot('VS008-register-767-typed'),
  vp(EDGE), wait(400), shot('VS008-register-768-control'), overflowInfo('ov3'),
] };

PLANS.p03_vs003_vs004 = { name: 'p03_vs003_vs004', viewport: DESK, steps: [
  goto('/recovery'), shot('VS003-recovery-desktop'), dump('d0'),
  type('#email-input', 'student01@example.com'),
  clickText('button', 'Отправить код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'afterRequest'),
  shot('VS003-recovery-code-desktop'), dump('d1'),
  // wrong code first (error visible), then Cancel returns to /login
  type('#code-input', '000000'),
  clickText('button', 'Ввести код'), wait(1800),
  shot('VS003-code-wrong-desktop'), toastState('t1'),
  clickText('button', 'Отмена'), wait(1200),
  evaljs(`(() => location.pathname)()`, 'afterCancel'),
  shot('VS003-cancel-back-login'),
  // redo flow with fresh code
  goto('/recovery'), type('#email-input', 'student01@example.com'),
  clickText('button', 'Отправить код'), wait(1800),
  { do: 'waitCodes', n: 2 },
  type('#code-input', '{CODE}'),
  clickText('button', 'Ввести код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'afterCorrectCode'),
  shot('VS004-reset-desktop-form'), dump('d2'),
  // terminal branch: tamper token, submit valid password
  setRecoveryToken('tampered-token-abc'),
  reload(),
  type('#password-input', 'Newpass123!'), type('#repeat-password-input', 'Newpass123!'),
  clickText('button', 'Сменить пароль'), wait(1800),
  shot('VS004-reset-desktop-terminal'), dump('d3'), toastState('t2'),
  evaljs(`(() => { const a = [...document.querySelectorAll('a')].map(x => x.innerText.trim()); return a; })()`, 'links'),
] };

PLANS.p04_vs011 = { name: 'p04_vs011', viewport: MOB, steps: [
  goto('/recovery'), shot('VS011-recovery-767'),
  type('#email-input', 'student01@example.com'),
  clickText('button', 'Отправить код'), wait(1800),
  { do: 'waitCodes', n: 1 },
  type('#code-input', '{CODE}'),
  clickText('button', 'Ввести код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'atReset'),
  shot('VS011-reset-767'), overflowInfo('ov1'), dump('d1'),
  // weak password -> field errors
  click('#password-input'), type('#password-input', '123'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b1'),
  wait(400), shot('VS011-reset-767-fielderrors'), dump('d2'),
  // terminal branch
  setRecoveryToken('tampered-token-xyz'), reload(),
  type('#password-input', 'Newpass123!'), type('#repeat-password-input', 'Newpass123!'),
  clickText('button', 'Сменить пароль'), wait(1800),
  shot('VS011-reset-767-terminal'), dump('d3'), overflowInfo('ov2'),
  evaljs(`(() => { const a = [...document.querySelectorAll('a')].map(x => x.innerText.trim()); return a; })()`, 'links'),
  // fresh flow at 768 control
  vp(EDGE), goto('/recovery'),
  type('#email-input', 'student01@example.com'),
  clickText('button', 'Отправить код'), wait(1800),
  { do: 'waitCodes', n: 2 },
  type('#code-input', '{CODE}'),
  clickText('button', 'Ввести код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'atReset768'),
  shot('VS011-reset-768-control'), overflowInfo('ov3'),
] };

PLANS.p05_vs009_vs010 = { name: 'p05_vs009_vs010', viewport: MOB, steps: [
  goto('/recovery'), shot('VS009-recovery-767'), overflowInfo('ov1'), dump('d0'),
  click('#email-input'), type('#email-input', 'not-an-email'), evaljs(`(() => { document.activeElement.blur(); return 'blur'; })()`, 'b1'),
  wait(400), shot('VS009-recovery-767-error'), dump('d1'), overflowInfo('ov2'),
  type('#email-input', 'student01@example.com'),
  clickText('button', 'Отправить код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'atCode'),
  shot('VS010-code-767'), overflowInfo('ov3'), dump('d2'),
  // wrong code -> inline error
  type('#code-input', '000000'),
  clickText('button', 'Ввести код'), wait(1800),
  shot('VS010-code-767-error'), dump('d3'), overflowInfo('ov4'),
  // 768 control on the same screen
  vp(EDGE), wait(400), shot('VS010-code-768-control'), overflowInfo('ov5'),
  vp(MOB), wait(300),
  // resend -> fresh code -> correct -> /reset-password
  clickText('button', 'Переотправить код'), wait(1800),
  { do: 'waitCodes', n: 2 },
  type('#code-input', '{CODE}'),
  clickText('button', 'Ввести код'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'afterResendCode'),
  shot('VS010-after-resend-nav'),
] };

PLANS.p06_vs005 = { name: 'p06_vs005', viewport: DESK, steps: [
  ...login('student01', 'student123!'),
  goto('/profile'), shot('VS005-profile-desktop'), dump('d1'),
  evaljs(`(() => [...document.querySelectorAll('input')].map(i => ({ id: i.id, readOnly: i.readOnly, disabled: i.disabled })))()`, 'inputs'),
  // save main data -> «Сохранено» toast top-right
  clickText('button', 'Сохранить'), wait(1800),
  shot('VS005-profile-desktop-saved'), toastState('t1'),
  // change password -> «Пароль изменён»
  type('#profile-current-password', 'student123!'), type('#profile-new-password', 'Newpass123!'), type('#profile-repeat-password', 'Newpass123!'),
  clickText('button', 'Сменить пароль'), wait(1800),
  shot('VS005-profile-desktop-passwd'), toastState('t2'),
  // wrong current password -> error toast
  type('#profile-current-password', 'badpass'), type('#profile-new-password', 'Anypass1!'), type('#profile-repeat-password', 'Anypass1!'),
  clickText('button', 'Сменить пароль'), wait(1800),
  shot('VS005-profile-desktop-passwd-err'), toastState('t3'),
  // mobile
  vp(MOB), goto('/profile'), shot('VS005-profile-mobile'), overflowInfo('ov1'),
  clickText('button', 'Сохранить'), wait(1800),
  shot('VS005-profile-mobile-saved'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner'); const form = document.querySelector('form'); ` +
    `return { banner: b ? { y: b.getBoundingClientRect().y, text: b.innerText.slice(0, 80), inHeaderHost: !!b.closest('app-header-notification') } : null, ` +
    `formY: form ? form.getBoundingClientRect().y : null }; })()`, 'mobileSuccessPos'),
  // wrong current password -> error under the password form
  type('#profile-current-password', 'badpass'), type('#profile-new-password', 'Anypass2!'), type('#profile-repeat-password', 'Anypass2!'),
  clickText('button', 'Сменить пароль'), wait(1800),
  shot('VS005-profile-mobile-passwd-err'),
  evaljs(`(() => { const bs = [...document.querySelectorAll('.app-notification-banner')]; ` +
    `const pwForm = [...document.querySelectorAll('form')].find(f => f.querySelector('#profile-current-password')); ` +
    `return { banners: bs.map(b => ({ y: Math.round(b.getBoundingClientRect().y), text: b.innerText.slice(0, 80), formHost: !!b.closest('form') })), pwFormY: pwForm ? Math.round(pwForm.getBoundingClientRect().y) : null }; })()`, 'mobileErrPos'),
] };

PLANS.p07_vs006 = { name: 'p07_vs006', viewport: DESK, steps: [
  goto('/login'), wait(600),
  evaljs(`(() => [...document.querySelectorAll('.app-topbar__tab, nav a')].map(a => a.innerText.trim()))()`, 'guestTabs'),
  shot('VS006-guest-header'),
  type('#login-input', 'student01'), type('#password-input', 'student123!'),
  click('button[type=submit]'), wait(1800),
  evaljs(`(() => [...document.querySelectorAll('.app-topbar__tab')].map(a => a.innerText.trim()))()`, 'studentTabs'),
  shot('VS006-student-header'),
  clickText('button', 'Выйти'), wait(1200),
  evaljs(`(() => location.pathname)()`, 'afterLogout'),
  type('#login-input', 'teacher'), type('#password-input', 'teacher123!'),
  click('button[type=submit]'), wait(1800),
  evaljs(`(() => [...document.querySelectorAll('.app-topbar__tab')].map(a => a.innerText.trim()))()`, 'teacherTabs'),
  shot('VS006-teacher-header'),
] };

/* ---------- Batch 2: works ---------- */

PLANS.p08_ts229 = { name: 'p08_ts229', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/works'), wait(1600),
  shot('TS229-works-desktop', true), dump('d1'),
  evaljs(`(() => { const btn = document.querySelector('.works-page__head button'); const card = document.querySelector('.works-page__card'); ` +
    `const rep = document.querySelector('.works-page__report'); const sel = document.querySelector('.works-page__toolbar .p-select label, .works-page__toolbar .p-select'); ` +
    `const main = document.querySelector('main'); return { btnBg: btn ? getComputedStyle(btn).backgroundColor : null, ` +
    `btnH: btn ? btn.getBoundingClientRect().height : null, cardRadius: card ? getComputedStyle(card).borderRadius : null, ` +
    `report: rep ? rep.innerText : null, selectText: sel ? sel.innerText.slice(0, 40) : null, ` +
    `mainPadLeft: main ? getComputedStyle(main).paddingLeft : null, mainMarginLeft: main ? getComputedStyle(main).marginLeft : null }; })()`, 'styles'),
  evaljs(`(() => { const edit = document.querySelector('.works-page__actions button:first-of-type'); ` +
    `const del = document.querySelector('.works-page__actions button:last-of-type'); ` +
    `const chk = document.querySelector('.works-page__list .p-checkbox input'); return { ` +
    `editTitle: edit ? (edit.title || edit.getAttribute('aria-label')) : null, ` +
    `delTitle: del ? (del.title || del.getAttribute('aria-label')) : null, ` +
    `editIcon: edit ? edit.querySelector('.pi')?.className : null, delIcon: del ? del.querySelector('.pi')?.className : null, ` +
    `checkboxDisabled: chk ? chk.disabled : null, rowCount: document.querySelectorAll('.works-page__list tbody tr').length }; })()`, 'rowInfo'),
  // hover edit button to try native title tooltip
  evaljs(`(() => { const b = document.querySelector('.works-page__actions button'); const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()`, 'hoverPt'),
] };

PLANS.p09_ts230_ts231 = { name: 'p09_ts230_ts231', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/works/new'), wait(1200),
  shot('TS230-labform-desktop'), dump('d0'),
  clickText('button', 'Сохранить'), wait(1200),
  shot('TS230-labform-errors'), dump('d1'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner, .p-toast-message'); ` +
    `const inv = [...document.querySelectorAll('.p-invalid, input.ng-invalid, textarea.ng-invalid')].map(e => e.id); ` +
    `return { banner: b ? b.innerText.slice(0, 100) : null, invalidFields: inv }; })()`, 'errState'),
  // delete dialog for lab №2 sem №1
  goto('/works'), wait(1600),
  delRowButton('2', '1'), wait(800),
  shot('TS231-delete-dialog'), dump('d2'),
  evaljs(`(() => { const dlg = document.querySelector('.p-confirmdialog, .p-dialog'); if (!dlg) return { present: false }; ` +
    `const btns = [...dlg.querySelectorAll('button')].map(b => b.innerText.trim()); ` +
    `return { present: true, text: dlg.innerText.slice(0, 200), btns }; })()`, 'dialog'),
  clickText('.p-dialog button, .p-confirmdialog button', 'No'), wait(600),
  evaljs(`(() => ({ dialogGone: !document.querySelector('.p-dialog-mask, .p-confirmdialog-mask') }))()`, 'dialogClosed'),
] };

PLANS.p10_ts232 = { name: 'p10_ts232', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/works'), wait(1600),
  tamperLabId(0, '00000000-0000-4000-8000-00000000dead'),
  reload(), wait(1600),
  delRowButton('1', '1'), wait(600),
  evaljs(`(() => { const rows = [...document.querySelectorAll('.p-confirmdialog button, .p-dialog button')]; const yes = rows.find(b => b.innerText.trim() === 'Yes'); if (yes) { yes.click(); return 'yes'; } return 'no-yes-btn'; })()`, 'yesClick'),
  wait(1800),
  shot('TS232-toast-desktop-immediately'), dump('d1'), toastState('t1'),
  wait(6000),
  shot('TS232-toast-desktop-after6s'), toastState('t2'),
] };

PLANS.p11_ts233 = { name: 'p11_ts233', viewport: MOB, steps: [
  ...login('teacher', 'teacher123!'),
  // duplicate lab -> 409 inline under form on /works/new
  goto('/works/new'), wait(1200),
  type('#lab-number', '1'), type('#lab-content', 'Дубль для проверки ошибки 409'),
  evaljs(`(() => { const el = document.getElementById('lab-semester'); const sel = el && el.closest('.p-select'); if (sel) { sel.click(); return 'opened'; } return 'no-select'; })()`, 'selOpen'),
  wait(500), clickText('.p-select-overlay .p-select-option', '1'), wait(400),
  clickText('button', 'Сохранить'), wait(2000),
  shot('TS233-new-767-error'), dump('d1'),
  evaljs(`(() => { const form = document.querySelector('form'); const b = [...document.querySelectorAll('.app-notification-banner, .p-toast-message')][0]; ` +
    `return { formBottom: Math.round(form.getBoundingClientRect().bottom), bannerTop: b ? Math.round(b.getBoundingClientRect().y) : null, ` +
    `text: b ? b.innerText.slice(0, 100) : null }; })()`, 'errPos'),
  // success on /works: delete row -> «Удалено» под шапкой
  goto('/works'), wait(1600),
  delRowButton('1', '1'), wait(600),
  evaljs(`(() => { const yes = [...document.querySelectorAll('.p-confirmdialog button, .p-dialog button')].find(b => b.innerText.trim() === 'Yes'); if (yes) { yes.click(); return 'yes'; } return 'no'; })()`, 'yesClick'),
  wait(2000),
  shot('TS233-works-767-success'), dump('d2'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner'); return b ? { text: b.innerText.slice(0, 60), inHeaderHost: !!b.closest('app-header-notification'), success: b.className.includes('success') } : null; })()`, 'succInfo'),
  // error on /works: tampered id -> 404 под шапкой
  tamperLabId(0, '00000000-0000-4000-8000-00000000dead'),
  reload(), wait(1600),
  delRowButton('1', '1'), wait(600),
  evaljs(`(() => { const yes = [...document.querySelectorAll('.p-confirmdialog button, .p-dialog button')].find(b => b.innerText.trim() === 'Yes'); if (yes) { yes.click(); return 'yes'; } return 'no'; })()`, 'yesClick2'),
  wait(2000),
  shot('TS233-works-767-error'), dump('d3'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner'); return b ? { text: b.innerText.slice(0, 60), inHeaderHost: !!b.closest('app-header-notification') } : null; })()`, 'errInfo'),
  // 768: toast top-right instead of inline
  vp(DESK), wait(600),
  tamperLabId(0, '00000000-0000-4000-8000-00000000beef'),
  reload(), wait(1600),
  delRowButton('1', '1'), wait(600),
  evaljs(`(() => { const yes = [...document.querySelectorAll('.p-confirmdialog button, .p-dialog button')].find(b => b.innerText.trim() === 'Yes'); if (yes) { yes.click(); return 'yes'; } return 'no'; })()`, 'yesClick3'),
  wait(2000),
  shot('TS233-works-768-toast'), toastState('t1'),
] };

/* ---------- Batch 3: student mobile, teacher sections ---------- */

PLANS.p12_ts381 = { name: 'p12_ts381', viewport: MOB, steps: [
  ...login('student01', 'student123!'),
  goto('/my-submissions'), wait(1800),
  shot('TS381-my-sub-767', true), dump('d1'), overflowInfo('ov1'),
  evaljs(`(() => { const cards = [...document.querySelectorAll('.my-submissions__card, [class*="card"]')].slice(0, 3).map(c => c.innerText.slice(0, 200)); ` +
    `return { cards, tablePresent: !!document.querySelector('table') }; })()`, 'cards'),
  vp(EDGE), wait(500),
  shot('TS381-my-sub-768', true), overflowInfo('ov2'), dump('d2'),
] };

PLANS.p13_ts382 = { name: 'p13_ts382', viewport: MOB, steps: [
  ...login('student01', 'student123!'),
  goto('/profile'), wait(1500),
  shot('TS382-profile-767', true), overflowInfo('ov1'), dump('d1'),
  evaljs(`(() => { const h = [...document.querySelectorAll('input')].map(i => Math.round(i.getBoundingClientRect().height)); ` +
    `const req = [...document.querySelectorAll('*')].find(e => e.children.length === 0 && /Требовани/.test(e.innerText || '')); ` +
    `return { inputHeights: h, requirementsVisible: !!req }; })()`, 'metrics'),
  vp(EDGE), wait(500),
  shot('TS382-profile-768', true), dump('d2'),
] };

PLANS.p14_ts383 = { name: 'p14_ts383', viewport: MOB, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/works'), wait(1500),
  evaljs(`(() => { const h = document.querySelector('.app-topbar'); const tabs = document.querySelector('.app-topbar__tabs'); ` +
    `const who = document.querySelector('.app-topbar__who'); return { headerH: Math.round(h.getBoundingClientRect().height), ` +
    `tabsScrollW: tabs.scrollWidth, tabsClientW: tabs.clientWidth, whoDisplay: who ? getComputedStyle(who).display : 'none', ` +
    `whoVisible: who ? who.offsetWidth > 0 && who.offsetHeight > 0 : false, brand: document.querySelector('.app-topbar__brand')?.innerText }; })()`, 'topbar767'),
  shot('TS383-topbar-767'),
  evaljs(`(() => { const tabs = document.querySelector('.app-topbar__tabs'); tabs.scrollLeft = tabs.scrollWidth; return tabs.scrollLeft; })()`, 'scrolledTo'),
  wait(300), shot('TS383-topbar-767-scrolled'),
  vp(EDGE), wait(500),
  evaljs(`(() => { const h = document.querySelector('.app-topbar'); const who = document.querySelector('.app-topbar__who'); ` +
    `return { headerH: Math.round(h.getBoundingClientRect().height), whoVisible: who ? who.offsetWidth > 0 : false, whoText: who?.innerText }; })()`, 'topbar768'),
  shot('TS383-topbar-768'),
] };

PLANS.p15_ts384 = { name: 'p15_ts384', viewport: MOB, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/submissions'), wait(2200),
  shot('TS384-submissions-767'), overflowInfo('ov1'),
  evaljs(`(() => { const wrap = document.querySelector('.submissions-page__card'); const inp = document.querySelectorAll('.submissions-page__card input').length; ` +
    `return { cardScrollW: wrap ? wrap.scrollWidth : null, cardClientW: wrap ? wrap.clientWidth : null, dpInputs: inp }; })()`, 'submMetrics'),
  goto('/groups'), wait(1800),
  shot('TS384-groups-767'), overflowInfo('ov2'),
  goto('/access'), wait(1800),
  shot('TS384-access-767'), overflowInfo('ov3'),
] };

PLANS.p16_ts385 = { name: 'p16_ts385', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/submissions'), wait(2200),
  // remove lab №1 sem 1 from mock db (grid already loaded), then set a date -> 404
  removeLabByNumber(1, 1),
  dateCell(0, 1), wait(600), pickDay('15'), wait(500),
  evaljs(`(() => { const t = document.querySelector('.p-datepicker-panel'); return t ? 'panel-open' : 'no-panel'; })()`, 'panelCheck'),
  shot('TS385-desktop-datepicker'),
  wait(2000),
  shot('TS385-desktop-toast'), dump('d1'), toastState('t1'),
  wait(6000), toastState('t2'),
  // second error, close via X
  removeLabByNumber(1, 2),
  dateCell(0, 1), wait(600), pickDay('16'), wait(2500),
  toastState('t3'),
  evaljs(`(() => { const b = document.querySelector('.p-toast button'); if (!b) return 'no-btn'; b.click(); return 'x-clicked'; })()`, 'xClick'),
  wait(400), shot('TS385-desktop-toast-closed-x'), toastState('t4'),
  // /access setGroup success -> NO notification (аменда 6)
  goto('/access'), wait(2000),
  evaljs(`(() => { const rows = [...document.querySelectorAll('tbody tr')]; const sel = rows[0] && rows[0].querySelector('.p-select'); if (!sel) return 'no-select'; sel.click(); return 'opened'; })()`, 'rowSelOpen'),
  wait(600), clickText('.p-select-overlay .p-select-option', 'ИК-223'), wait(1800),
  shot('TS385-access-desktop-after-setgroup'), toastState('t5'), dump('d2'),
  // mobile inline under header
  vp(MOB), goto('/submissions'), wait(2200),
  removeLabByNumber(1, 3),
  dateCell(0, 1), wait(600), pickDay('17'), wait(2500),
  shot('TS385-mobile-inline'), dump('d3'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner'); return b ? { text: b.innerText.slice(0, 80), inHeaderHost: !!b.closest('app-header-notification'), y: Math.round(b.getBoundingClientRect().y) } : null; })()`, 'mobileInline'),
] };

PLANS.p17_ts386 = { name: 'p17_ts386', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/submissions'), wait(2500),
  shot('TS386-submissions-desktop', true), dump('d1'),
  evaljs(`(() => { const heads = [...document.querySelectorAll('thead th')].slice(0, 8).map(th => ({ t: th.innerText.trim(), colspan: th.colSpan })); ` +
    `const cap = document.querySelector('[data-test=range-caption]'); const sel = [...document.querySelectorAll('.submissions-page__toolbar .p-select')].map(s => s.innerText.slice(0, 30)); ` +
    `const row1 = document.querySelector('tbody tr'); const cells = row1 ? [...row1.cells].slice(0, 5).map(td => td.innerText.trim() || td.querySelector('input')?.value || '') : []; ` +
    `return { heads, caption: cap ? cap.innerText : null, selects: sel, row1: cells }; })()`, 'gridInfo'),
] };

PLANS.p18_ts387 = { name: 'p18_ts387', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/groups'), wait(1800),
  shot('TS387-groups-list', true), dump('d1'),
  evaljs(`(() => { const heads = [...document.querySelectorAll('thead th')].map(th => th.innerText.trim()); ` +
    `const rows = [...document.querySelectorAll('tbody tr')].map(r => r.innerText.trim().slice(0, 60)); return { heads, rows }; })()`, 'listInfo'),
  clickText('button', 'Создать группу'), wait(800),
  shot('TS387-create-dialog'), dump('d2'),
  evaljs(`(() => { const dlg = document.querySelector('.p-dialog'); return dlg ? { text: dlg.innerText.slice(0, 200), btns: [...dlg.querySelectorAll('button')].map(b => b.innerText.trim()) } : null; })()`, 'createDlg'),
  clickText('.p-dialog button', 'Отмена'), wait(600),
  // group card ИК-221
  clickText('tbody tr a, tbody tr', 'ИК-221'), wait(1800),
  evaljs(`(() => location.pathname)()`, 'atGroupCard'),
  shot('TS387-group-card', true), dump('d3'),
  evaljs(`(() => { const heads = [...document.querySelectorAll('thead th')].map(th => th.innerText.trim()); ` +
    `const crumbs = [...document.querySelectorAll('a, span, nav *')].map(e => e.innerText?.trim()).filter(t => t === 'Группы' || /ИК-221/.test(t || '')).slice(0, 6); ` +
    `const cnt = [...document.querySelectorAll('*')].find(e => e.children.length === 0 && /Студентов/.test(e.innerText || '')); ` +
    `return { heads, crumbs, count: cnt ? cnt.innerText : null }; })()`, 'cardInfo'),
] };

PLANS.p19_ts388 = { name: 'p19_ts388', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/access'), wait(2200),
  shot('TS388-access-desktop', true), dump('d1'),
  evaljs(`(() => { const search = document.querySelector('input[placeholder*="Поиск"]'); ` +
    `const chk = [...document.querySelectorAll('label, span')].find(e => e.innerText?.trim() === 'Без группы'); ` +
    `const heads = [...document.querySelectorAll('thead th')].map(th => th.innerText.trim()); ` +
    `const rowSels = document.querySelectorAll('tbody .p-select').length; ` +
    `const cap = document.querySelector('[class*="caption"], [class*="report"]'); ` +
    `const rows = [...document.querySelectorAll('tbody tr')].map(r => r.innerText.replace(/\\n/g, ' | ').slice(0, 120)); ` +
    `const noneCell = [...document.querySelectorAll('tbody td')].find(td => td.innerText.trim() === 'Без группы'); ` +
    `return { searchPh: search ? search.placeholder : null, noGroupChk: !!chk, heads, rowSels, caption: cap ? cap.innerText : null, rows: rows.slice(0, 4), ` +
    `noneCellStyle: noneCell ? getComputedStyle(noneCell).color + '/op:' + getComputedStyle(noneCell).opacity : null }; })()`, 'accessInfo'),
] };

/* ---- final fix-ups ---- */

PLANS.p21c_ts233 = { name: 'p21c_ts233', viewport: MOB, steps: [
  ...login('teacher', 'teacher123!'),
  // client-validation error on /works/new -> inline banner directly under form
  goto('/works/new'), wait(1400),
  clickText('button', 'Сохранить'), wait(1200),
  shot('TS233-new-767-error'), dump('d1'),
  evaljs(`(() => { const form = document.querySelector('form'); const b = [...document.querySelectorAll('.app-notification-banner, .p-toast-message')][0]; ` +
    `return { formBottom: Math.round(form.getBoundingClientRect().bottom), bannerTop: b ? Math.round(b.getBoundingClientRect().y) : null, ` +
    `text: b ? b.innerText.slice(0, 100) : null, inForm: b ? !!b.closest('form') : null, hasIcon: b ? !!b.querySelector('.pi') : null, hasX: b ? !!b.querySelector('.pi-times, [class*=close]') : null }; })()`, 'errPos'),
  // 768: same submit -> toast expected top-right (broken per VBUG-001) - control evidence
  vp(DESK), goto('/works/new'), wait(1200),
  clickText('button', 'Сохранить'), wait(1200),
  shot('TS233-new-768-control'), toastState('t1'),
] };

PLANS.p16b_ts385 = { name: 'p16b_ts385', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/access'), wait(2200),
  evaljs(`(() => { const rows = [...document.querySelectorAll('tbody tr')]; const sel = rows[0] && rows[0].querySelector('.p-select'); if (!sel) return 'no-select'; sel.click(); return 'opened'; })()`, 'rowSelOpen'),
  wait(700),
  evaljs(`(() => { const opts = [...document.querySelectorAll('.p-overlay .p-select-option, .p-select-option')]; return opts.length ? opts.map(o => o.innerText.trim()).slice(0, 6) : 'no-options-overlay-empty'; })()`, 'rowOptions'),
  shot('TS385-access-rowselect-desktop'),
  evaljs(`(() => location.pathname)()`, 'atAccess'),
  // mobile inline check via /access is notification-free by amenda 6; header-host check with a success instead
  vp(MOB), goto('/profile'), wait(1500),
  clickText('button', 'Сохранить'), wait(1800),
  shot('TS385-mobile-success-under-header'), dump('d1'),
  evaljs(`(() => { const b = document.querySelector('.app-notification-banner'); return b ? { text: b.innerText.slice(0, 40), inHeaderHost: !!b.closest('app-header-notification'), y: Math.round(b.getBoundingClientRect().y) } : null; })()`, 'mobSuccess'),
] };

PLANS.p22_ts388_muted = { name: 'p22_ts388_muted', viewport: DESK, steps: [
  ...login('teacher', 'teacher123!'),
  goto('/access'), wait(2200),
  evaljs(`(() => { const lbl = [...document.querySelectorAll('label, span')].find(e => e.innerText?.trim() === 'Без группы' && e.querySelector('input, .p-checkbox, ~ input')); const box = document.querySelector('.p-checkbox'); if (box) { box.click(); return 'checkbox-clicked'; } return 'no-checkbox'; })()`, 'chkClick'),
  wait(1800),
  shot('TS388-access-no-group-filter', true), dump('d1'),
  evaljs(`(() => { const rows = [...document.querySelectorAll('tbody tr')].map(r => r.innerText.replace(/\\n/g, ' | ').slice(0, 110)); ` +
    `const noneCell = [...document.querySelectorAll('tbody td')].find(td => td.innerText.trim() === 'Без группы'); ` +
    `const normalCell = [...document.querySelectorAll('tbody td')].find(td => /ИК-\\d+/.test(td.innerText.trim())); ` +
    `return { rows: rows.slice(0, 4), none: noneCell ? { color: getComputedStyle(noneCell).color, opacity: getComputedStyle(noneCell).opacity } : null, ` +
    `normal: normalCell ? { color: getComputedStyle(normalCell).color, opacity: getComputedStyle(normalCell).opacity } : null }; })()`, 'mutedInfo'),
] };

/* ---------- driver ---------- */

async function main() {
  const planName = process.argv[2];
  const plan = PLANS[planName];
  if (!plan) { console.error('UNKNOWN PLAN ' + planName); process.exit(1); }
  const out = { plan: planName, steps: [], console: [], pageerrors: [], failedRequests: [], codes: [] };

  const browser = await puppeteer.launch({
    executablePath: '/snap/bin/chromium',
    headless: 'new',
    args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--lang=ru-RU'],
    defaultViewport: plan.viewport,
  });
  const page = await browser.newPage();
  page.on('console', (msg) => {
    const t = msg.type(); const text = msg.text();
    if (t === 'error' || text.includes('mock-email')) {
      out.console.push({ type: t, text: String(text).slice(0, 400) });
      if (text.includes('mock-email')) {
        const m = text.match(/(\d{6})/);
        if (m) out.codes.push(m[1]);
      }
    }
  });
  page.on('pageerror', (e) => out.pageerrors.push(String(e).slice(0, 400)));
  page.on('requestfailed', (r) => {
    const u = r.url();
    if (!u.startsWith('data:')) out.failedRequests.push({ url: u.slice(0, 180), err: r.failure() && r.failure().errorText });
  });

  const settle = async (ms) => { await new Promise((r) => setTimeout(r, ms)); };

  try {
    for (let i = 0; i < plan.steps.length; i++) {
      const step = plan.steps[i];
      const rec = { i, step: Object.assign({}, step) };
      try {
        switch (step.do) {
          case 'viewport':
            await page.setViewport(step.viewport); await settle(step.wait || 400); break;
          case 'goto':
            await page.goto('http://localhost:4300' + step.url, { waitUntil: 'networkidle2', timeout: 45000 });
            await settle(step.wait || 700); break;
          case 'reload':
            await page.reload({ waitUntil: 'networkidle2', timeout: 45000 }); await settle(step.wait || 900); break;
          case 'wait': await settle(step.ms); break;
          case 'waitCodes': {
            const t0 = Date.now();
            while (out.codes.length < (step.n || 1) && Date.now() - t0 < 8000) await new Promise((r) => setTimeout(r, 200));
            if (out.codes.length < (step.n || 1)) throw new Error('no code captured, have: ' + out.codes.join(','));
            break;
          }
          case 'type': {
            await page.waitForSelector(step.sel, { timeout: 10000 });
            const text = String(step.text).replace('{CODE}', out.codes[out.codes.length - 1] || '');
            await page.click(step.sel, { clickCount: 3 });
            await page.keyboard.press('Backspace');
            await page.type(step.sel, text, { delay: 8 });
            break;
          }
          case 'click':
            await page.waitForSelector(step.sel, { timeout: 10000 });
            await page.click(step.sel); break;
          case 'clickText': {
            await page.waitForSelector(step.sel, { timeout: 10000 });
            const ok = await page.evaluate(({ sel, text }) => {
              const els = Array.from(document.querySelectorAll(sel));
              const el = els.reverse().find((e) => (e.textContent || '').trim().includes(text));
              if (el) { el.click(); return true; } return false;
            }, { sel: step.sel, text: step.text });
            if (!ok) throw new Error('clickText not found: ' + step.text + ' in ' + step.sel);
            break;
          }
          case 'eval': {
            let src = step.js;
            if (src.startsWith('(() =>') && src.endsWith(')()')) src = src.slice(0, -2); // IIFE -> plain arrow
            out[step.as || 'e' + i] = await page.evaluate(new Function('return (' + src + ')')());
            break;
          }
          case 'screenshot': {
            const file = path.join(SHOTS, step.name.endsWith('.png') ? step.name : step.name + '.png');
            if (step.clip) await page.screenshot({ path: file, clip: step.clip });
            else await page.screenshot({ path: file, fullPage: !!step.fullPage });
            rec.shot = file; break;
          }
          case 'textDump':
            out[step.as || 'text'] = await page.evaluate(() => document.body.innerText.slice(0, 2600));
            break;
        }
        rec.ok = true;
      } catch (e) {
        rec.ok = false; rec.error = String(e).slice(0, 250);
      }
      out.steps.push(rec);
    }
  } finally { await browser.close(); }

  fs.writeFileSync(path.join(LOGS, planName + '.out.json'), JSON.stringify(out, null, 1));
  const fails = out.steps.filter((s) => !s.ok).map((s) => ({ i: s.i, do: s.step.do, err: s.error }));
  console.log(JSON.stringify({ plan: planName, fails, codes: out.codes, pageErrors: out.pageerrors.length, failedReq: out.failedRequests.length }));
}

main().catch((e) => { console.error('DRIVER-FAIL', e); process.exit(1); });
