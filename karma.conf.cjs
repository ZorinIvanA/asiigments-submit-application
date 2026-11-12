/**
 * Karma-конфигурация основного проекта (asiigments-submit-application).
 *
 * Нужна ради кастомного лаунчера ChromeHeadlessNoSandbox: в CI и контейнерах
 * sandbox Chrome недоступен, а встроенный лаунчер ChromeHeadless флаг
 * --no-sandbox не передаёт. Локально `npm test` (браузер ChromeHeadless)
 * работает по-прежнему: билдер инжектирует browsers поверх этого файла.
 *
 * Файл подключён в angular.json (проект asiigments-submit-application →
 * test → options.karmaConfig). При наличии karmaConfig билдер
 * (@angular/build:karma) не применяет встроенные значения frameworks/plugins
 * — они заданы здесь; basePath/files/singleRun инжектируются билдером.
 * Паттерн взят из tests/integration/B-17/karma.conf.cjs (урок BL-001).
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
          '--remote-debugging-port=0',
        ],
      },
    },
    // Дефолтный порт кармы: зоны B-17/B-18 сидят на 9917/9918, основной
    // проект занимает 9876 и не пересекается с ними.
    port: 9876,
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
