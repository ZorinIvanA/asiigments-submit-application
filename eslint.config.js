/**
 * Конфигурация ESLint (flat config) для клиента: TypeScript-код приложения
 * и Angular-шаблоны. Паттерн — из документации angular-eslint v20
 * (пакет единый, переискспортирует парсер и правила шаблонов).
 *
 * Проверка без type-aware анализа (recommended, не recommendedTypeChecked):
 * не требует проектов tsconfig и быстрее работает в CI.
 */
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = tseslint.config(
  // Линтим только код приложения (src/client): зоны интеграционных тестов
  // в tests/ — замороженные артефакты сданных лаб, их проверяют прогоны.
  {
    ignores: [
      'dist/**',
      'coverage/**',
      '.angular/**',
      '.pipeline/**',
      'tests/**',
    ],
  },
  {
    // TypeScript: базовые правила + recommended typescript-eslint +
    // recommended angular-eslint. processInlineTemplates позволяет линтить
    // инлайн-шаблоны компонентов вместе с TS-файлом.
    files: ['src/client/**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // Подчёркнутый префикс — конвенция «намеренно не используется»
      // (заглушки тестовых двойников).
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
      // Пустой interface с единственным extends — маркерный тип в models.ts
      // (например, Teacher extends User); пустые «настоящие» интерфейсы
      // остаются под запретом.
      '@typescript-eslint/no-empty-object-type': [
        'error',
        { allowInterfaces: 'with-single-extends' },
      ],
    },
  },
  {
    // Внешние Angular-шаблоны (*.html): recommended + accessibility-правила.
    files: ['src/client/**/*.html'],
    extends: [
      ...angular.configs.templateRecommended,
      ...angular.configs.templateAccessibility,
    ],
    rules: {
      // Поля форм — PrimeNG-компоненты (p-select и т.п.): правило не видит
      // связь label[for] ↔ [inputId] кастомных контролов и ложно срабатывает.
      '@angular-eslint/template/label-has-associated-control': 'off',
    },
  },
);
