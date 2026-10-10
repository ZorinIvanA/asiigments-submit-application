/**
 * TS-180 (P1, FR-093 AC «Проксирование», автоматизированная часть):
 * статическая проверка dev-прокси Angular на Kestrel.
 *
 * given — в src/client подключён proxy.conf к ng serve;
 * when  — проверка конфигурации прокси;
 * then  — маршруты /api пересылаются на базовый URL Kestrel
 *         http://localhost:5080 с changeOrigin; SHOULD — также /health;
 *         proxy.conf подключён к ng serve в angular.json.
 *
 * Границы автоматизации (по кейсу): полный браузерный прогон «запрос входа
 * (POST /auth/login с базовым префиксом API) через прокси из браузера
 * доходит до Kestrel, Set-Cookie возвращается и отправляется последующими
 * запросами» — ручной E2E, вне области автотеста; здесь проверяется
 * статическая конфигурация.
 *
 * Файлы читаются импортом (esbuild-бандл Karma): живой fs в браузере
 * недоступен. Пути от каталога этого spec: workspace root — на три вверх.
 */
import proxyConf from '../../../src/client/proxy.conf.json';
import workspaceConfig from '../../../angular.json';

interface ProxyRoute {
  target?: string;
  changeOrigin?: boolean;
}

/** Kestrel dev-адрес (ASM-002/FR-093) — единственный ожидаемый target. */
const KESTREL_BASE = 'http://localhost:5080';

describe('TS-180: dev-прокси Angular на Kestrel — статика конфигурации (FR-093)', () => {
  it('proxy.conf.json: /api → http://localhost:5080 с changeOrigin', () => {
    const api = proxyConf['/api'] as ProxyRoute | undefined;
    expect(api).withContext('маршрут /api описан в proxy.conf.json').toBeDefined();
    expect(api!.target).withContext('target — Kestrel 5080 (ASM-002)').toBe(KESTREL_BASE);
    expect(api!.changeOrigin).withContext('changeOrigin включён (FR-093)').toBeTrue();
  });

  it('proxy.conf.json (SHOULD FR-093): /health проксируется на тот же Kestrel', () => {
    const health = proxyConf['/health'] as ProxyRoute | undefined;
    expect(health).withContext('SHOULD FR-093: маршрут /health описан').toBeDefined();
    expect(health!.target).withContext('target /health — тот же Kestrel').toBe(KESTREL_BASE);
    expect(health!.changeOrigin).withContext('changeOrigin для /health').toBeTrue();
  });

  it('proxy.conf подключён к ng serve в angular.json (serve → proxyConfig)', () => {
    const projects = workspaceConfig.projects as Record<
      string,
      {
        architect?: {
          serve?: {
            options?: { proxyConfig?: string };
          };
        };
      }
    >;
    const app = projects['asiigments-submit-application'];
    expect(app).withContext('проект клиента описан').toBeDefined();
    const proxyConfig = app.architect?.serve?.options?.proxyConfig;
    expect(proxyConfig)
      .withContext('ng serve использует src/client/proxy.conf.json')
      .toBe('src/client/proxy.conf.json');
  });
});
