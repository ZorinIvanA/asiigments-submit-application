/**
 * Юнит-тесты конфигурации приложения: русская локаль, тема PrimeNG Aura,
 * русские переводы PrimeNG (требования FR-001/NFR-004 на уровне каркаса),
 * провайдер анимаций для PrimeNG-оверлеев (VBUG-001/VBUG-002, NG05105)
 * и HTTP-ядро с интерцептором аутентификации (T-017, FR-091/FR-092):
 * provideHttpClient(withInterceptors([authInterceptor])). Мок-слой удалён
 * вместе с его инициализатором (FR-026(1), T-022/T-023): спек утверждает
 * ОТСУТСТВИЕ инициализатора мок-слоя в фазе APP_INITIALIZER (сид и
 * мок-сессия не записываются) и успешный бутстрап с холодным стартом
 * сессии (AuthService.initSession, FR-092).
 */
import { formatDate } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  ANIMATION_MODULE_TYPE,
  Component,
  LOCALE_ID,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { firstValueFrom } from 'rxjs';

import Aura from '@primeuix/themes/aura';
import { MessageService } from 'primeng/api';
import { PrimeNG } from 'primeng/config';
import { Toast } from 'primeng/toast';

import { API_BASE_URL } from '../api-base-url';
import { appConfig } from './app.config';
import { PRIME_NG_RU } from './primeng-ru';

/**
 * Хост с <p-toast/> — тот же PrimeNG-оверлей, чьи synthetic-слушатели
 * (@toastAnimation.start) вызывали NG05105 без провайдера анимаций.
 */
@Component({
  imports: [Toast],
  providers: [MessageService],
  template: '<p-toast />',
})
class ToastHost {}

describe('appConfig — конфигурация каркаса', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
  });

  it('провайдер LOCALE_ID установлен в ru', () => {
    expect(TestBed.inject(LOCALE_ID)).toBe('ru');
  });

  it('данные локали ru зарегистрированы через registerLocaleData', () => {
    // Без registerLocaleData(localeRu) форматирование с 'ru' бросает
    // ошибку «Missing locale data for the locale ru».
    expect(formatDate(new Date(2026, 0, 15), 'dd.MM.yyyy', 'ru')).toBe(
      '15.01.2026',
    );
  });

  it('PrimeNG инициализирован с пресетом на базе Aura (кастомизация ADR-108)', () => {
    const primeNg = TestBed.inject(PrimeNG);
    // T-109: пресет строится definePreset(Aura, …) — дерево секций Aura
    // сохранено (semantic/components), заменой темы оно не становится.
    const preset = primeNg.theme()?.preset as
      | {
          primitive?: Record<string, unknown>;
          semantic?: Record<string, unknown>;
          components?: Record<string, unknown>;
        }
      | undefined;
    expect(preset).withContext('кастомный пресет определён').toBeTruthy();
    expect(preset!.semantic).withContext('секция semantic из Aura').toBeDefined();
    expect(preset!.components).withContext('секция components из Aura').toBeDefined();
    // definePreset не трогает primitive-секцию: она обязана совпасть с базой
    // Aura (усиление ассерта — ревью CR-002 T-109).
    expect(preset!.primitive)
      .withContext('primitive-секция без изменений от базы Aura')
      .toEqual(Aura.primitive as Record<string, unknown>);
  });

  it('PrimeNG инициализирован с русскими переводами', () => {
    const primeNg = TestBed.inject(PrimeNG);
    expect(primeNg.translation).toEqual(PRIME_NG_RU);
  });

  it('кнопки подтверждающих диалогов — «Yes»/«No» (исключение NFR-004)', () => {
    const primeNg = TestBed.inject(PrimeNG);
    expect(primeNg.translation.accept).toBe('Yes');
    expect(primeNg.translation.reject).toBe('No');
  });
});

describe('appConfig — провайдер анимаций (VBUG-001/VBUG-002, NG05105)', () => {
  it('provideAnimationsAsync включён: ANIMATION_MODULE_TYPE — BrowserAnimations', () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    // Без провайдера анимаций токен не задан вовсе — именно это приводило
    // к NG05105 и неработающим оверлеям (Toast, ConfirmDialog, Select).
    expect(TestBed.inject(ANIMATION_MODULE_TYPE)).toBe('BrowserAnimations');
  });

  it('рендер p-toast (@toastAnimation.start) не бросает NG05105', () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    const fixture = TestBed.createComponent(ToastHost);
    // Регистрация synthetic-слушателя @toastAnimation.start обычным
    // DOM-рендерером бросает NG05105; с provideAnimationsAsync рендер
    // проходит без ошибок.
    expect(() => fixture.detectChanges()).not.toThrow();
    expect(fixture.nativeElement.querySelector('.p-toast'))
      .withContext('контейнер тостов в DOM')
      .not.toBeNull();
  });

  it('паттерн unit-тестов [...appConfig.providers, provideNoopAnimations()] перекрывает анимации', () => {
    // Тесты TestBed по кодовой базе добавляют provideNoopAnimations ПОСЛЕ
    // appConfig.providers: последний провайдер побеждает, noop-анимации
    // сохраняются (детерминизм юнит-тестов не ломается).
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideNoopAnimations()],
    });
    expect(TestBed.inject(ANIMATION_MODULE_TYPE)).toBe('NoopAnimations');
  });
});

describe('appConfig — HTTP-ядро (T-017, IF-014, FR-091/FR-092)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let apiBase: string;

  beforeEach(() => {
    // provideHttpClientTesting ПОСЛЕ appConfig.providers — как provideNoop
    // -Animations: последний провайдер HttpBackend побеждает, цепочка
    // интерцепторов из provideHttpClient(withInterceptors([authInterceptor]))
    // сохраняется (проверка поведения конфигурации, а не деталей provide*).
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    // Фаза инициализации бутстрапа (APP_INITIALIZER) выполняется TestBed при
    // первом обращении к инжектору — как bootstrapApplication. Единственный
    // HTTP фазы — холодный старт сессии (AuthService.initSession, GET
    // /auth/me, FR-092): уходит в тестовый бэкенд, перехватываем его здесь.
    httpMock.expectOne(`${apiBase}/auth/me`).flush(null);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса (в т.ч. POST /auth/logout).
    httpMock.verify();
    // Чистка storage — изоляция от соседних spec-файлов (конвенция спек).
    localStorage.clear();
  });

  it('provideHttpClient включён: HttpClient инъекцируется, API-запрос проходит через authInterceptor (withCredentials)', async () => {
    const pending = firstValueFrom(http.get(`${apiBase}/labs`));

    const request = httpMock.expectOne(`${apiBase}/labs`);
    // withCredentials выставляет именно authInterceptor из конфигурации
    // appConfig — без него API-запрос ушёл бы без cookie-аутентификации.
    expect(request.request.withCredentials)
      .withContext('интерцептор подключён провайдерами appConfig')
      .toBeTrue();
    request.flush({ items: [], total: 0, page: 1, pageSize: 10 });

    await expectAsync(pending).toBeResolved();
  });

  it('инициализатор мок-слоя отсутствует (FR-026(1)): фаза APP_INITIALIZER стартует сессию без мок-сида, бутстрап успешен', async () => {
    // Фаза инициализации бутстрапа выполнена TestBed при первом обращении к
    // инжектору (beforeEach): единственный HTTP фазы — холодный старт сессии
    // (AuthService.initSession → GET /auth/me), и он ушёл через интерцептор
    // appConfig в тестовый бэкенд, а не в мок. Инициализатора мок-слоя в
    // фазе больше нет — его отсутствие подтверждается поведением: ни сид
    // мок-БД, ни мок-сессия, которые писал прошлый инициализатор, не
    // записаны (FR-026(1)/(4)); проверяем по произвольным legacy-ключам.
    expect(localStorage.getItem('legacy.db.v1'))
      .withContext('мок-сид не записан (инициализатор мока удалён)')
      .toBeNull();
    expect(localStorage.getItem('legacy.session.userId'))
      .withContext('мок-сессия не записана (признак сессии только в памяти, FR-092)')
      .toBeNull();

    // Успешный бутстрап: после фазы инициализации HTTP-цепочка жива — запрос
    // через интерцептор appConfig разрешается тестовым бэкендом без ошибок.
    let failure: unknown = null;
    const pending = firstValueFrom(http.get(`${apiBase}/auth/me`)).then(
      () => undefined,
      (error: unknown) => (failure = error),
    );
    const request = httpMock.expectOne(`${apiBase}/auth/me`);
    expect(request.request.withCredentials).toBeTrue();
    request.flush(null);
    await pending;
    expect(failure).withContext('запрос через цепочку appConfig разрешён').toBeNull();
  });
});
