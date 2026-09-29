#!/usr/bin/env python3
"""Generate JSON plans for visual re-check cycle 2."""
import json, os

TOOLS = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(TOOLS, 'plans')
os.makedirs(OUT, exist_ok=True)

DESK = {"width": 1280, "height": 900}
MOB = {"width": 767, "height": 900}
EDGE = {"width": 768, "height": 900}

def wait(ms): return {"do": "wait", "ms": ms}
def shot(name, fullPage=False): return {"do": "screenshot", "name": name, "fullPage": fullPage}
def goto(url, waitFor=None, tmo=30000): return {"do": "goto", "url": url, "waitFor": waitFor, "timeout": tmo}
def vp(viewport): return {"do": "viewport", "viewport": viewport, "wait": 500}
def type_(sel, text): return {"do": "type", "sel": sel, "text": text}
def click(sel): return {"do": "click", "sel": sel}
def clickText(sel, text, idx=0): return {"do": "clickText", "sel": sel, "text": text, "idx": idx}
def ev(js, as_): return {"do": "eval", "js": js, "as": as_}
def dump(as_="text"): return {"do": "textDump", "as": as_}
def reload(waitFor=None, ms=1000): return {"do": "reload", "waitFor": waitFor, "wait": ms}
def press(key): return {"do": "press", "key": key}
def hover(sel): return {"do": "hover", "sel": sel}
def focus(sel): return {"do": "focus", "sel": sel}
def clear(): return {"do": "clearStorage"}

def login(u, p):
    return [
        goto('/login', waitFor='#login-input'),
        type_('#login-input', u),
        type_('#password-input', p),
        click('button[type=submit]'),
        wait(1700),
    ]

TAMPER_LAB = ("(() => { const db = JSON.parse(localStorage.getItem('mock.db.v1')); "
              "const lab = db.labs.find(l => l.semester === %d && l.number === %d); "
              "if (!lab) return 'no-lab'; lab.id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; "
              "localStorage.setItem('mock.db.v1', JSON.stringify(db)); return 'ok'; })()")

TOAST_INFO = ("(() => { const ts = [...document.querySelectorAll('.p-toast-message')]; "
              "return ts.map(t => { const r = t.getBoundingClientRect(); "
              "return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), "
              "text: t.innerText.slice(0, 200), cls: t.className.slice(0, 160), "
              "icon: !!t.querySelector('.p-toast-message-icon'), "
              "close: !!(t.querySelector('.p-toast-icon-close') || t.querySelector('.p-toast-close-button') || t.querySelector('button')) }; }); })()")

BANNER_INFO = ("(() => { const els = [...document.querySelectorAll('div,section,p,span')].filter(e => "
  "e.children.length === 0 && e.textContent.trim().length > 2 && e.textContent.trim().length < 200 && "
  "(e.className || '').toString().match(/(banner|anchor|notification)/)); "
  "return els.map(e => ({ cls: e.className.toString().slice(0, 120), text: e.textContent.trim().slice(0, 120), "
  "y: Math.round(e.getBoundingClientRect().top) })); })()")

BANNER_FIND = ("(() => { const els = [...document.querySelectorAll('.app-notification-banner, [class*=banner]')].filter(e => "
  "e.offsetParent !== null); const head = document.querySelector('.app-topbar'); "
  "return { headBottom: head ? Math.round(head.getBoundingClientRect().bottom) : null, "
  "banners: els.map(e => ({ cls: e.className.toString().slice(0, 90), "
  "text: e.innerText.trim().slice(0, 100), y: Math.round(e.getBoundingClientRect().top), "
  "icon: !!e.querySelector('.pi, svg, [class*=icon]'), close: !!e.querySelector('button') })), "
  "toasts: document.querySelectorAll('.p-toast-message').length }; })()")

plans = {}

# ---------------- p01: VS-001 + VS-007 (login desktop/mobile error) ----------------
plans['p01_vs001_vs007'] = dict(name='p01_vs001_vs007', viewport=DESK, steps=[
    clear(),
    goto('/login', waitFor='#login-input'),
    shot('VS001-login-desktop-initial'),
    type_('#login-input', 'student01'),
    type_('#password-input', 'wrong-pass-1'),
    click('button[type=submit]'),
    wait(1600),
    shot('VS001-login-desktop-error'),
    ev(TOAST_INFO, 'toast'),
    ev("(() => ({ hasText: document.body.innerText.includes('Неверный логин или пароль') }))()", 'textCheck'),
    click('.p-toast-close-button, .p-toast-icon-close'),
    wait(600),
    shot('VS001-login-desktop-error-closed'),
    ev(TOAST_INFO, 'toastAfterClose'),
    # VS-007: mobile width
    vp(MOB),
    goto('/login', waitFor='#login-input'),
    type_('#login-input', 'student02'),
    type_('#password-input', 'wrong-pass-2'),
    click('button[type=submit]'),
    wait(1600),
    shot('VS007-login-mobile-error'),
    ev(TOAST_INFO, 'mobileToast'),
    ev(("(() => { const form = document.querySelector('form'); "
        "const err = [...document.querySelectorAll('*')].find(e => e.children.length === 0 && "
        "e.textContent.trim() === 'Неверный логин или пароль'); "
        "if (!err) return { found: false }; const re = err.getBoundingClientRect(); "
        "const fe = form ? form.getBoundingClientRect() : null; "
        "return { found: true, errY: Math.round(re.y), errH: Math.round(re.height), "
        "formBottom: fe ? Math.round(fe.bottom) : null, visible: re.width > 0 && re.height > 0 }; })()"), 'mobileErrPos'),
    vp(DESK),
    wait(400),
    type_('#login-input', 'student03'),
    type_('#password-input', 'wrong-pass-3'),
    click('button[type=submit]'),
    wait(1600),
    shot('VS007-login-desktop-error-banner'),
    ev(TOAST_INFO, 'deskToast'),
])

# ---------------- p02: VS-002 register ----------------
plans['p02_vs002'] = dict(name='p02_vs002', viewport=DESK, steps=[
    clear(),
    goto('/register', waitFor='#full-name-input'),
    shot('VS002-register-desktop-initial'),
    dump('initialText'),
    ev("(() => ({ selects: document.querySelectorAll('select, .p-select').length, "
       "roleText: document.body.innerText.includes('Роль') }))()", 'roleCheck'),
    click('button[type=submit]'),
    wait(900),
    shot('VS002-register-desktop-empty-errors'),
    ev("(() => ({ errCount: document.querySelectorAll('.ng-invalid, [class*=error]').length }))()", 'emptyErr'),
    type_('#full-name-input', 'Визуал Тест Визуалович'),
    type_('#login-input', 'vsreg01'),
    type_('#email-input', 'bad-email'),
    type_('#password-input', '123'),
    type_('#repeat-password-input', '456'),
    click('button[type=submit]'),
    wait(900),
    shot('VS002-register-desktop-invalid'),
    ev("(() => document.body.innerText.slice(0, 1500))()", 'invalidText'),
    # existing email 409
    {"do": "setValue", "sel": "#email-input", "value": "student01@example.com"},
    {"do": "setValue", "sel": "#password-input", "value": "ValidPass1!"},
    {"do": "setValue", "sel": "#repeat-password-input", "value": "ValidPass1!"},
    click('button[type=submit]'),
    wait(1800),
    shot('VS002-register-409-email'),
    ev(TOAST_INFO, 'toastEmail409'),
    ev("(() => ({ t: document.body.innerText.includes('Пользователь с таким email уже существует') }))()", 'email409Check'),
    # existing login 409
    {"do": "setValue", "sel": "#login-input", "value": "student02"},
    {"do": "setValue", "sel": "#email-input", "value": "vsreg01@example.com"},
    click('button[type=submit]'),
    wait(1800),
    shot('VS002-register-409-login'),
    ev(TOAST_INFO, 'toastLogin409'),
    ev("(() => ({ t: document.body.innerText.includes('Пользователь с таким логином уже существует') }))()", 'login409Check'),
])

# ---------------- p03: VS-003 + VS-004 recovery + reset ----------------
plans['p03_vs003_vs004'] = dict(name='p03_vs003_vs004', viewport=DESK, steps=[
    clear(),
    goto('/recovery', waitFor='#email-input'),
    shot('VS003-recovery-desktop'),
    type_('#email-input', 'student01@example.com'),
    click('button[type=submit]'),
    wait(1800),
    ev("(() => location.pathname)()", 'afterRequestPath'),
    shot('VS003-recovery-code-desktop'),
    dump('codePageText'),
    ev("(() => ({ resend: document.body.innerText.includes('Переотправить код'), "
       "cancel: document.body.innerText.includes('Отмена') }))()", 'linksCheck'),
    # Отмена -> /login
    clickText('a, button', 'Отмена'),
    wait(1200),
    ev("(() => location.pathname)()", 'afterCancelPath'),
    shot('VS003-cancel-back-login'),
    # again: request code, wrong code first
    goto('/recovery', waitFor='#email-input'),
    type_('#email-input', 'student01@example.com'),
    click('button[type=submit]'),
    wait(1800),
    ev("(() => { const db = JSON.parse(localStorage.getItem('mock.db.v1')); "
       "const c = db.recoveryCodes.filter(c => !c.usedAt).pop(); return c ? c.code : 'none'; })()", 'codeValue'),
    focus('#code-input'),
    {"do": "typePage", "text": "000000"},
    click('button[type=submit]'),
    wait(1500),
    shot('VS003-code-wrong-desktop'),
    ev("(() => ({ t: document.body.innerText.includes('Код восстановления не подходит') }))()", 'wrongCodeCheck'),
    # correct code -> /reset-password
    {"do": "setValueJs", "sel": "#code-input", "js": "(JSON.parse(localStorage.getItem('mock.db.v1')).recoveryCodes.filter(c => !c.usedAt).pop() || {}).code || '000000'"},
    click('button[type=submit]'),
    wait(1800),
    ev("(() => location.pathname)()", 'afterCodePath'),
    shot('VS003-reset-password-reached'),
    # VS-004: form inspection
    wait(500),
    shot('VS004-reset-desktop-form'),
    dump('resetFormText'),
    # terminal branch: bad resetToken
    clear(),
    ev("(() => { sessionStorage.setItem('recovery.flow.v1', JSON.stringify({ email: 'student01@example.com', "
       "resetToken: 'expired-or-invalid-token' })); return 'set'; })()", 'setBadToken'),
    goto('/reset-password', waitFor='#password-input'),
    {"do": "setValue", "sel": "#password-input", "value": "NewPass123!"},
    {"do": "setValue", "sel": "#repeat-password-input", "value": "NewPass123!"},
    click('button[type=submit]'),
    wait(1800),
    shot('VS004-reset-desktop-terminal'),
    ev("(() => ({ banner: document.body.innerText.includes('Ссылка восстановления недействительна или истекла'), "
       "link: document.body.innerText.includes('Запросить код заново'), "
       "formOpen: !!document.querySelector('#password-input') }))()", 'terminalCheck'),
])

# ---------------- p04: VS-005 profile ----------------
plans['p04_vs005'] = dict(name='p04_vs005', viewport=DESK, steps=[
    clear(),
    *login('student01', 'student123!'),
    goto('/profile', waitFor='.profile-page, main, h1'),
    shot('VS005-profile-desktop'),
    dump('profileText'),
    ev("(() => ({ loginIsValue: !!document.querySelector('[data-test=login-value]'), "
       "loginInput: !!document.querySelector('#profile-login-input, input[formcontrolname=login]') }))()", 'loginRO'),
    clickText('button', 'Сохранить'),
    wait(1600),
    shot('VS005-profile-desktop-saved'),
    ev(TOAST_INFO, 'savedToast'),
    # password change: wrong current
    {"do": "setValue", "sel": "#profile-current-password", "value": "badpass1!"},
    {"do": "setValue", "sel": "#profile-new-password", "value": "NewPass123!"},
    {"do": "setValue", "sel": "#profile-repeat-password", "value": "NewPass123!"},
    clickText('button', 'Сменить пароль'),
    wait(1600),
    shot('VS005-profile-desktop-passwd-err'),
    ev(TOAST_INFO, 'pwErrToast'),
    ev(BANNER_INFO, 'pwErrBanner'),
    ev("(() => document.body.innerText.includes('Неверный текущий пароль'))()", 'pwWrongCurrentText'),
    # correct change (set values exactly)
    {"do": "setValue", "sel": "#profile-current-password", "value": "student123!"},
    {"do": "setValue", "sel": "#profile-new-password", "value": "NewPass123!"},
    {"do": "setValue", "sel": "#profile-repeat-password", "value": "NewPass123!"},
    clickText('button', 'Сменить пароль'),
    wait(1800),
    shot('VS005-profile-desktop-passwd'),
    ev(TOAST_INFO, 'pwOkToast'),
    # mobile
    clear(),
    vp(MOB),
    *login('student01', 'student123!'),
    goto('/profile', waitFor='.profile-page, main, h1'),
    clickText('button', 'Сохранить'),
    wait(1600),
    shot('VS005-profile-mobile-saved'),
    ev(TOAST_INFO, 'mobileSavedToast'),
    ev(BANNER_INFO, 'mobileSavedBanner'),
    ev("(() => { const h = document.querySelector('.app-topbar'); const t = document.querySelector('.p-toast-message'); "
       "if (!h || !t) return { topbar: !!h, toast: !!t }; return { topbarBottom: Math.round(h.getBoundingClientRect().bottom), "
       "toastTop: Math.round(t.getBoundingClientRect().top) }; })()", 'mobileToastPos'),
    {"do": "setValue", "sel": "#profile-current-password", "value": "badpass1!"},
    {"do": "setValue", "sel": "#profile-new-password", "value": "NewPass123!"},
    {"do": "setValue", "sel": "#profile-repeat-password", "value": "NewPass123!"},
    {"do": "scrollTo", "sel": "[data-test=password-card-title]"},
    clickText('button', 'Сменить пароль'),
    wait(1600),
    shot('VS005-profile-mobile-passwd-err', fullPage=True),
    ev(TOAST_INFO, 'mobilePwErrToast'),
    ev(BANNER_INFO, 'mobilePwErrBanner'),
])

# ---------------- p05: VS-006 header tabs by role ----------------
plans['p05_vs006'] = dict(name='p05_vs006', viewport=DESK, steps=[
    clear(),
    goto('/login', waitFor='#login-input'),
    ev("(() => [...document.querySelectorAll('.guest-topbar__tab')].map(a => a.textContent.trim()))()", 'guestTabs'),
    shot('VS006-guest-header'),
    ev("(() => [...document.querySelectorAll('.guest-topbar__tab')].map(a => ({ t: a.textContent.trim(), "
       "href: a.getAttribute('href') })))()", 'guestTabLinks'),
    # student
    type_('#login-input', 'student01'),
    type_('#password-input', 'student123!'),
    click('button[type=submit]'),
    wait(1700),
    ev("(() => [...document.querySelectorAll('.app-topbar__tab')].map(a => a.textContent.trim()))()", 'studentTabs'),
    shot('VS006-student-header'),
    # teacher (logout first, no reload)
    click('.app-topbar__logout'),
    wait(1500),
    ev("(() => [...document.querySelectorAll('.guest-topbar__tab')].map(a => a.textContent.trim()))()", 'guestTabsAfterLogout'),
    type_('#login-input', 'teacher'),
    type_('#password-input', 'teacher123!'),
    click('button[type=submit]'),
    wait(1700),
    ev("(() => [...document.querySelectorAll('.app-topbar__tab')].map(a => a.textContent.trim()))()", 'teacherTabs'),
    shot('VS006-teacher-header'),
])

# ---------------- p06: TS-229 + TS-230 + TS-231 works ----------------
plans['p06_works'] = dict(name='p06_works', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    goto('/works', waitFor='.works-page table'),
    shot('TS229-works-desktop'),
    dump('worksText'),
    ev("(() => { const heads = [...document.querySelectorAll('.works-page th')].map(t => t.textContent.trim()); "
       "const cbs = [...document.querySelectorAll('.works-page p-checkbox input, .works-page .p-checkbox input')]; "
       "const delBtns = document.querySelectorAll(\"button[title='Удалить']\").length; "
       "const penBtns = document.querySelectorAll(\"button[title='Редактировать']\").length; "
       "const report = (document.querySelector('.works-page__report') || {}).textContent || null; "
       "const sel = !!document.querySelector('.works-page__toolbar .p-select'); "
       "const btn = document.querySelector('.p-select-label'); "
       "return { heads, checkboxes: cbs.length, allDisabled: cbs.every(c => c.disabled), delBtns, penBtns, report, semesterSelect: sel, "
       "selectLabel: btn ? btn.textContent.trim() : null }; })()", 'worksStructure'),
    # semester dropdown opens (special check)
    click('.works-page__toolbar .p-select'),
    wait(900),
    shot('TS229-semester-select-open'),
    ev("(() => { const o = document.querySelector('.p-select-overlay'); "
       "return o ? { visible: getComputedStyle(o).display !== 'none', options: o.innerText.slice(0, 120) } : 'no-overlay'; })()", 'selectOverlay'),
    press('Escape'),
    wait(400),
    # lab form
    goto('/works/new', waitFor='form'),
    shot('TS230-labform-desktop'),
    dump('labFormText'),
    hover('.pi-link'),
    wait(900),
    shot('TS230-chain-tooltip'),
    ev("(() => { const t = document.querySelector('.p-tooltip'); return t ? t.innerText : 'no-tooltip'; })()", 'tooltipText'),
    # semester select in form opens
    click('form .p-select'),
    wait(900),
    shot('TS230-select-open'),
    ev("(() => { const o = document.querySelector('.p-select-overlay'); "
       "return o ? { visible: getComputedStyle(o).display !== 'none', opts: o.innerText.slice(0, 100) } : 'no-overlay'; })()", 'formSelectOverlay'),
    press('Escape'),
    wait(300),
    clickText('button', 'Сохранить'),
    wait(1200),
    shot('TS230-labform-errors'),
    ev("(() => ({ banner: document.body.innerText.includes('Данные заполнены неверно'), "
       "invalid: document.querySelectorAll('form .ng-invalid').length }))()", 'labFormErrCheck'),
    # delete dialog
    goto('/works', waitFor='.works-page table'),
    wait(600),
    ev("(() => { const rows = [...document.querySelectorAll('.works-page tbody tr')]; "
       "const row = rows.find(r => r.cells[0].innerText.trim() === '2' && r.cells[2].innerText.trim() === '1'); "
       "if (!row) return 'row-not-found'; const b = (() => { const bs = row.querySelectorAll('p-button button'); return bs[bs.length - 1]; })(); "
       "if (!b) return 'no-btn'; b.click(); return 'clicked'; })()", 'delClick'),
    wait(1000),
    shot('TS231-delete-dialog'),
    ev("(() => { const d = document.querySelector('.p-confirmdialog'); if (!d) return 'no-dialog'; "
       "const msg = (d.querySelector('.p-confirmdialog-message') || {}).textContent || ''; "
       "const btns = [...d.querySelectorAll('button')].map(b => b.textContent.trim()); "
       "const locked = !!document.querySelector('.works-page__list--locked'); "
       "return { msg: msg.trim(), btns, locked }; })()", 'dialogCheck'),
    ev(TOAST_INFO, 'noToastOnDialog'),
    clickText('.p-confirmdialog button', 'No'),
    wait(900),
    shot('TS231-after-no'),
    ev("(() => ({ dialogGone: !document.querySelector('.p-confirmdialog'), "
       "row2: [...document.querySelectorAll('.works-page tbody tr')].some(r => r.cells[0].innerText.trim() === '2') }))()", 'afterNoCheck'),
])

# ---------------- p07: TS-232 desktop error toast (409 duplicate) ----------------
plans['p07_ts232'] = dict(name='p07_ts232', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    goto('/works/new', waitFor='#lab-number'),
    type_('#lab-number', '1'),
    click('form .p-select'),
    wait(800),
    ev("(() => { const o = document.querySelector('.p-select-overlay'); if (!o) return 'no-overlay'; "
       "const opt = [...o.querySelectorAll('.p-select-option')].find(li => li.textContent.trim() === '1'); "
       "if (!opt) return 'no-opt:' + o.innerText.slice(0, 40); opt.click(); return 'picked'; })()", 'pickSem'),
    wait(500),
    type_('#lab-content', 'Дубль для тоста ошибки'),
    clickText('button', 'Сохранить'),
    wait(2000),
    shot('TS232-toast-desktop-immediately'),
    ev(TOAST_INFO, 'toastNow'),
    ev("(() => { const t = document.querySelector('.p-toast-message'); if (!t) return 'none'; "
       "return { bg: getComputedStyle(t).backgroundColor }; })()", 'toastStyle'),
    wait(6000),
    shot('TS232-toast-desktop-after6s'),
    ev("(() => [...document.querySelectorAll('.p-toast-message')].length)()", 'toastAfter6s'),
])

# ---------------- p08: TS-233 mobile inline banners ----------------
plans['p08_ts233'] = dict(name='p08_ts233', viewport=MOB, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    # /works/new invalid submit -> inline banner under form
    goto('/works/new', waitFor='form'),
    clickText('button', 'Сохранить'),
    wait(1200),
    shot('TS233-new-767-error'),
    ev(TOAST_INFO, 'newToast'),
    ev(BANNER_FIND, 'newErrPos'),
    # /works success banner under header (delete lab 2 sem 1)
    goto('/works', waitFor='.works-page table'),
    wait(600),
    ev("(() => { const rows = [...document.querySelectorAll('.works-page tbody tr')]; "
       "const row = rows.find(r => r.cells[0].innerText.trim() === '2' && r.cells[2].innerText.trim() === '1'); "
       "if (!row) return 'no-row'; const bs = row.querySelectorAll('p-button button'); bs[bs.length-1].click(); return 'ok'; })()", 'delOkClick'),
    wait(800),
    clickText('.p-confirmdialog button', 'Yes'),
    wait(1500),
    shot('TS233-works-767-success'),
    ev(BANNER_FIND, 'worksSuccessPos'),
    # 404 deep-link: единственный достижимый серверный 404 -> баннер на форме
    goto('/works/new?id=aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', waitFor='form'),
    wait(1800),
    shot('TS233-works-404-767'),
    ev(BANNER_FIND, 'works404Pos'),
    # 768 control: toast top-right (success after delete + form error)
    vp(EDGE),
    goto('/works', waitFor='.works-page table'),
    wait(600),
    ev("(() => { const rows = [...document.querySelectorAll('.works-page tbody tr')]; "
       "const row = rows.find(r => r.cells[0].innerText.trim() === '3' && r.cells[2].innerText.trim() === '1'); "
       "if (!row) return 'no-row'; const bs = row.querySelectorAll('p-button button'); bs[bs.length-1].click(); return 'ok'; })()", 'delOk2'),
    wait(800),
    clickText('.p-confirmdialog button', 'Yes'),
    wait(1500),
    shot('TS233-works-768-toast'),
    ev(TOAST_INFO, 'edgeToast'),
    goto('/works/new', waitFor='form'),
    clickText('button', 'Сохранить'),
    wait(1200),
    shot('TS233-new-768-control'),
    ev(TOAST_INFO, 'edgeNewToast'),
])

# ---------------- p09: TS-386 desktop submissions ----------------
plans['p09_ts386'] = dict(name='p09_ts386', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    {"do": "goto", "url": "/submissions", "waitFor": ".submissions-page tbody tr", "timeout": 40000},
    shot('TS386-submissions-desktop'),
    dump('subsText'),
    ev("(() => { const labHeads = [...document.querySelectorAll('.submissions-page thead th')].map(t => "
       "(t.textContent || '').trim()); const rows = document.querySelectorAll('.submissions-page tbody tr').length; "
       "const caption = (document.querySelector('[data-test=range-caption]') || {}).textContent || null; "
       "const labels = [...document.querySelectorAll('.submissions-page__toolbar label')].map(l => l.textContent.trim()); "
       "const pickers = document.querySelectorAll('.submissions-page app-submission-date-cell input').length; "
       "return { headSample: labHeads.slice(0, 8), labHeadCount: labHeads.length, rows, caption, labels, pickers }; })()", 'subsStructure'),
    ev("(() => { const inputs = [...document.querySelectorAll('.submissions-page app-submission-date-cell input')]; "
       "return { filled: inputs.filter(i => i.value).slice(0, 6).map(i => i.value), "
       "emptySample: inputs.filter(i => !i.value).length, "
       "dashes: inputs.filter(i => i.value.includes('—') || i.value.includes('-') && !/^\\d{2}\\.\\d{2}\\.\\d{4}$/.test(i.value)).length }; })()", 'subsCells'),
    click('#submissions-group + .p-select, .submissions-page__toolbar .p-select'),
    wait(900),
    shot('TS386-group-select-open'),
    ev("(() => { const o = document.querySelector('.p-select-overlay'); return o ? o.innerText.slice(0, 80) : 'none'; })()", 'groupOverlay'),
    press('Escape'),
    wait(300),
    ev("(() => { const sels = document.querySelectorAll('.submissions-page__toolbar .p-select'); "
       "return { count: sels.length }; })()", 'selCount'),
])

# ---------------- p10: TS-384 submissions 767 + 200 pickers + text dates ----------------
plans['p10_ts384'] = dict(name='p10_ts384', viewport=MOB, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    {"do": "goto", "url": "/submissions", "waitFor": ".submissions-page tbody tr", "timeout": 40000},
    shot('TS384-submissions-767'),
    ev("(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth, "
       "pickers: document.querySelectorAll('.submissions-page app-submission-date-cell input').length }))()", 'subs767'),
    ev("(() => { const wrap = document.querySelector('.submissions-page__card, .submissions-page'); "
       "return wrap ? { sw: wrap.scrollWidth, cw: wrap.clientWidth } : 'no-wrap'; })()", 'subsScroll'),
    # text input into EMPTY cell: row 3 (student03), first input (Лаб1 Сдача)
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[2]; "
       "const inp = tr.querySelectorAll('app-submission-date-cell input')[0]; inp.focus(); return 'focused'; })()", 'focusEmpty'),
    {"do": "typePage", "text": "15.10.2026"},
    press('Enter'),
    press('Escape'),
    wait(1600),
    shot('TS384-date-typed-empty'),
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[2]; "
       "return tr.querySelectorAll('app-submission-date-cell input')[0].value; })()", 'emptyCellValue'),
    # text input into FILLED cell: row 1 student01 Лаб1 Сдача (01.09.2026)
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[0]; "
       "const inp = tr.querySelectorAll('app-submission-date-cell input')[0]; inp.focus(); inp.select(); return inp.value; })()", 'filledFocus'),
    {"do": "typePage", "text": "20.09.2026"},
    press('Enter'),
    press('Escape'),
    wait(1600),
    shot('TS384-date-typed-filled'),
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[0]; "
       "return tr.querySelectorAll('app-submission-date-cell input')[0].value; })()", 'filledCellValue'),
    reload(waitFor='.submissions-page tbody tr', ms=1500),
    shot('TS384-after-reload'),
    ev("(() => { const rows = [...document.querySelectorAll('.submissions-page tbody tr')]; "
       "const r1 = rows[0].querySelectorAll('app-submission-date-cell input')[0].value; "
       "const r3 = rows[2].querySelectorAll('app-submission-date-cell input')[0].value; "
       "return { student01lab1: r1, student03lab1: r3 }; })()", 'afterReloadValues'),
])

# ---------------- p11: TS-385 error on date save + access no-notify ----------------
plans['p11_ts385'] = dict(name='p11_ts385', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    ev(TAMPER_LAB % (1, 20), 'tamper'),
    {"do": "goto", "url": "/submissions", "waitFor": ".submissions-page tbody tr", "timeout": 40000},
    wait(800),
    # last lab (20) Sдача cell of row 1 = input index (20-1)*2 = 38
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[0]; "
       "const inps = tr.querySelectorAll('app-submission-date-cell input'); inps[38].focus(); "
       "return { count: inps.length }; })()", 'focusTampered'),
    {"do": "typePage", "text": "10.11.2026"},
    press('Enter'),
    press('Escape'),
    wait(1800),
    shot('TS385-toast-desktop'),
    ev(TOAST_INFO, 'errToast'),
    click('.p-toast-close-button, .p-toast-icon-close'),
    wait(600),
    shot('TS385-toast-desktop-closed'),
    ev(TOAST_INFO, 'errToastClosed'),
    # /access: successful setGroup -> NO notification
    goto('/access', waitFor='.access__search-input'),
    type_('.access__search-input', 'student31'),
    wait(1400),
    shot('TS385-access-before'),
    ev("(() => { const row = [...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('student31')); "
       "if (!row) return 'no-row'; const sel = row.querySelector('.p-select'); if (!sel) return 'no-select'; "
       "sel.click(); return 'opened'; })()", 'rowSelectOpen'),
    wait(900),
    ev("(() => { const o = document.querySelector('.p-select-overlay'); if (!o) return 'no-overlay'; "
       "const opt = [...o.querySelectorAll('li, .p-select-option')].find(li => li.textContent.includes('ИК-222')); "
       "if (!opt) return { opts: o.innerText.slice(0, 100) }; opt.click(); return 'picked'; })()", 'pickGroup'),
    wait(1800),
    shot('TS385-access-after-set'),
    ev("(() => ({ toasts: document.querySelectorAll('.p-toast-message').length, "
       "row: [...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('student31'))?.innerText.slice(0, 120) }))()", 'accessAfter'),
    # mobile inline under header
    clear(),
    vp(MOB),
    *login('teacher', 'teacher123!'),
    ev(TAMPER_LAB % (1, 20), 'tamperM'),
    {"do": "goto", "url": "/submissions", "waitFor": ".submissions-page tbody tr", "timeout": 40000},
    wait(800),
    ev("(() => { const tr = document.querySelectorAll('.submissions-page tbody tr')[0]; "
       "const inps = tr.querySelectorAll('app-submission-date-cell input'); inps[38].focus(); return inps.length; })()", 'focusTamperedM'),
    {"do": "typePage", "text": "11.11.2026"},
    press('Enter'),
    press('Escape'),
    wait(1800),
    shot('TS385-mobile-inline-banner'),
    ev(BANNER_FIND, 'mobileBannerPos'),
    ev(TOAST_INFO, 'mobileToastCheck'),
])

# ---------------- p12: TS-387 groups ----------------
plans['p12_ts387'] = dict(name='p12_ts387', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    goto('/groups', waitFor='table'),
    shot('TS387-groups-list'),
    dump('groupsText'),
    ev("(() => { const heads = [...document.querySelectorAll('th')].map(t => t.textContent.trim()); "
       "const rows = [...document.querySelectorAll('tbody tr')].map(r => r.innerText.replace(/\\t/g, ' | ').slice(0, 60)); "
       "return { heads, rows }; })()", 'groupsStructure'),
    clickText('button', 'Создать группу'),
    wait(1000),
    shot('TS387-create-dialog'),
    ev("(() => { const d = document.querySelector('.p-dialog'); if (!d) return 'no-dialog'; "
       "return { header: (d.querySelector('.p-dialog-title') || {}).textContent, "
       "hasName: !!d.querySelector('#group-create-name'), "
       "btns: [...d.querySelectorAll('button')].map(b => b.textContent.trim()).filter(Boolean) }; })()", 'createDialogCheck'),
    clickText('.p-dialog button', 'Отмена'),
    wait(800),
    shot('TS387-create-dialog-closed'),
    ev("(() => ({ gone: !document.querySelector('.p-dialog') }))()", 'dialogClosed'),
    ev("(() => { const row = [...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('ИК-221')); "
       "if (!row) return 'no-row'; const link = row.querySelector('a') || row.querySelector('button'); "
       "(link || row).click(); return 'clicked'; })()", 'openIk221'),
    wait(1600),
    ev("(() => location.pathname)()", 'groupCardPath'),
    shot('TS387-group-card'),
    dump('groupCardText'),
    ev("(() => ({ crumbs: document.body.innerText.includes('Группы') && document.body.innerText.includes('ИК-221'), "
       "count25: document.body.innerText.includes('Студентов: 25'), "
       "exclude: document.body.innerText.includes('Исключить из группы') }))()", 'groupCardCheck'),
])

# ---------------- p13: TS-388 access desktop ----------------
plans['p13_ts388'] = dict(name='p13_ts388', viewport=DESK, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    goto('/access', waitFor='.access__search-input'),
    shot('TS388-access-desktop'),
    dump('accessText'),
    ev("(() => { const inp = document.querySelector('.access__search-input'); const r = inp.getBoundingClientRect(); "
       "const cs = getComputedStyle(inp); return { width: Math.round(r.width), placeholder: inp.placeholder, "
       "font: cs.fontSize }; })()", 'searchInfo'),
    ev("(() => { const heads = [...document.querySelectorAll('th')].map(t => t.textContent.trim()); "
       "const rowSels = document.querySelectorAll('tbody .p-select').length; "
       "const caption = (document.querySelector('.access__report, [class*=report]'))?.textContent || "
       "[...document.querySelectorAll('span,p')].map(e => e.textContent).find(t => t.includes('Показать записи')) || null; "
       "return { heads, rowSels, caption }; })()", 'accessStructure'),
    ev("(() => { const cbs = document.querySelector('input[type=checkbox]'); if (!cbs) return 'no-cb'; "
       "cbs.click(); return 'clicked'; })()", 'clickNoGroup'),
    wait(1400),
    shot('TS388-access-no-group-filter'),
    ev("(() => { const rows = [...document.querySelectorAll('tbody tr')]; "
       "return { count: rows.length, texts: rows.map(r => r.innerText.replace(/\\t/g, ' | ').slice(0, 80)) }; })()", 'noGroupRows'),
    ev("(() => { const cbs = document.querySelector('input[type=checkbox]'); cbs.click(); return 'un'; })()", 'uncheck'),
    wait(1200),
    type_('.access__search-input', 'student31'),
    wait(1400),
    shot('TS388-access-search'),
    ev("(() => [...document.querySelectorAll('tbody tr')].map(r => r.innerText.replace(/\\t/g, ' | ').slice(0, 80)))()", 'searchRows'),
])

# ---------------- p14: TS-381 my-submissions 767/768 ----------------
plans['p14_ts381'] = dict(name='p14_ts381', viewport=MOB, steps=[
    clear(),
    *login('student01', 'student123!'),
    goto('/my-submissions', waitFor='table, .my-submissions'),
    shot('TS381-my-sub-767'),
    dump('mySub767Text'),
    ev("(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }))()", 'ov767'),
    vp(EDGE),
    goto('/my-submissions', waitFor='table, .my-submissions'),
    shot('TS381-my-sub-768'),
    ev("(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }))()", 'ov768'),
])

# ---------------- p15: TS-382 profile mobile regression ----------------
plans['p15_ts382'] = dict(name='p15_ts382', viewport=MOB, steps=[
    clear(),
    *login('student01', 'student123!'),
    goto('/profile', waitFor='.profile-page, h1'),
    shot('TS382-profile-767'),
    ev("(() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }))()", 'ov767'),
    vp(EDGE),
    goto('/profile', waitFor='.profile-page, h1'),
    shot('TS382-profile-768'),
])

# ---------------- p16: TS-383 topbar mobile/desktop ----------------
plans['p16_ts383'] = dict(name='p16_ts383', viewport=MOB, steps=[
    clear(),
    *login('teacher', 'teacher123!'),
    shot('TS383-topbar-767'),
    ev("(() => { const tb = document.querySelector('.app-topbar'); const r = tb.getBoundingClientRect(); "
       "const tabs = tb.querySelector('.app-topbar__tabs'); "
       "return { height: Math.round(r.height), tabCount: tb.querySelectorAll('.app-topbar__tab').length, "
       "whoVisible: !!tb.querySelector('.app-topbar__who') && getComputedStyle(tb.querySelector('.app-topbar__who')).display !== 'none', "
       "brand: (tb.querySelector('.app-topbar__brand') || {}).textContent, "
       "tabsScrollW: tabs ? tabs.scrollWidth : null, tabsClientW: tabs ? tabs.clientWidth : null }; })()", 'topbar767'),
    ev("(() => { const tabs = document.querySelector('.app-topbar__tabs'); tabs.scrollLeft = tabs.scrollWidth; "
       "return { scrolled: tabs.scrollLeft }; })()", 'scrollTabs'),
    wait(600),
    shot('TS383-topbar-767-scrolled'),
    vp(EDGE),
    wait(600),
    shot('TS383-topbar-768'),
    ev("(() => { const tb = document.querySelector('.app-topbar'); "
       "return { height: Math.round(tb.getBoundingClientRect().height), "
       "who: (tb.querySelector('.app-topbar__who') || {}).textContent || 'hidden' }; })()", 'topbar768'),
])

# ---------------- p17: VS-009 recovery mobile regression ----------------
plans['p17_vs009'] = dict(name='p17_vs009', viewport=MOB, steps=[
    clear(),
    goto('/recovery', waitFor='#email-input'),
    shot('VS009-recovery-767'),
    type_('#email-input', 'not-an-email'),
    click('button[type=submit]'),
    wait(900),
    shot('VS009-recovery-767-error'),
    ev("(() => ({ err: document.body.innerText.includes('Введите корректный email'), "
       "scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth }))()", 'recovery767'),
    vp(EDGE),
    goto('/recovery', waitFor='#email-input'),
    shot('VS009-recovery-768'),
])

for name, plan in plans.items():
    with open(os.path.join(OUT, name + '.json'), 'w') as f:
        json.dump(plan, f, ensure_ascii=False, indent=1)
print('wrote', len(plans), 'plans to', OUT)
