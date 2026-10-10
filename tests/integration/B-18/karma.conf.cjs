/**
 * Karma-конфигурация интеграционной зоны батча B-18 (изоляция прогона,
 * урок BL-001): выделенный порт 9918 — параллельные батчи держат дефолтный
 * 9876 (B-06) и свой 9917 (B-17); кастомный лаунчер ChromeHeadlessNoSandbox
 * для headless-прогона без sandbox (в CI/контейнерах sandbox Chrome
 * недоступен).
 *
 * Файл подключён в angular.json (project integration-b18 → test →
 * options.karmaConfig). Builder (@angular/build:karma) при наличии
 * karmaConfig не применяет встроенные значения frameworks/plugins —
 * они заданы здесь; basePath/files/singleRun/browsers инжектируются
 * билдером поверх этого файла.
 */
module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine'],
    plugins: [
      require.resolve('karma-jasmine'),
      require.resolve('karma-chrome-launcher'),
    ],
    customLaunchers: {
      ChromeHeadlessNoSandbox: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu'],
      },
    },
    // Изоляция батча: свой порт, чтобы не пересекаться с параллельными
    // прогонами соседних зон (9876, 9917).
    port: 9918,
    reporters: ['progress'],
    logLevel: config.LOG_WARN,
    autoWatch: false,
    browserNoActivityTimeout: 60000,
  });
};
