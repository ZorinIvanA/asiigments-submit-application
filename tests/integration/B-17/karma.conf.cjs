/**
 * Karma-конфигурация интеграционной зоны батча B-17 (изоляция прогона,
 * урок BL-001): выделенный порт 9917 — параллельные батчи держат дефолтный
 * 9876; кастомный лаунчер ChromeHeadlessNoSandbox для headless-прогона
 * без sandbox (в CI/контейнерах sandbox Chrome недоступен).
 *
 * Файл подключён в angular.json (project integration-b17 → test →
 * options.karmaConfig).Builder (@angular/build:karma) при наличии
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
        flags: [
          '--no-sandbox',
          '--disable-dev-shm-usage',
          '--disable-gpu',
          // Эфемерный devtools-порт: дефолт 9222 конфликтует с браузерами
          // параллельных прогонов соседних зон (урок BL-001).
          '--remote-debugging-port=0',
        ],
      },
    },
    // Изоляция батча: свой порт, чтобы не пересекаться с параллельными
    // прогонами соседних зон (9876).
    port: 9917,
    reporters: ['progress'],
    logLevel: config.LOG_WARN,
    autoWatch: false,
    restartOnFileChange: false,
    retryLimit: 0,
    browserDisconnectTolerance: 0,
    captureTimeout: 30000,
    browserNoActivityTimeout: 60000,
    processKillTimeout: 5000,
  });
};
