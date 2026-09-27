# Сдача лабораторных работ — каркас приложения

Демо-приложение для учёта сдачи лабораторных работ (frontend-only, браузерный
мок-слой). Текущее состояние — каркас Angular-приложения; функциональные слои
(мок-БД, сервисы, экраны, валидаторы) добавляются последующими задачами.

## Стек

- Angular 20+, standalone-компоненты, TypeScript strict
- SCSS, единственный брейкпоинт проекта — **768px** (граница мобильной вёрстки)
- PrimeNG 20+ с темой Aura (`@primeuix/themes`, только светлый режим) и primeicons
- Русская локаль: `LOCALE_ID = ru`, `registerLocaleData(localeRu)`, русские
  переводы встроенных текстов PrimeNG (`core/config/primeng-ru.ts`)
- Тесты: Karma + Jasmine (ChromeHeadless)

## Установка и запуск

Требуется Node.js LTS (20.19+ или 22.12+) и npm; установлен Google Chrome
(для unit-тестов в ChromeHeadless).

```bash
npm install
npm start
```

После запуска приложение открывается по адресу **http://localhost:4200**
(пока рендерится пустой каркас с `<router-outlet />`).

Прочие команды:

```bash
npm test           # unit-тесты (Karma + Jasmine, ChromeHeadless)
npm run build      # production-сборка (ng build --configuration production)
npm run watch      # пересборка разработки при изменениях
```

## Структура каталогов (NFR-003)

Внутри `src/app` код располагается только в пяти ветках; иные отклонения
недопустимы. Файлы бутстрапа сознательно разложены по тем же веткам:
маршруты и конфигурация — это «конфиг» (core), корневой компонент — «оболочка»
(layout).

```
src/app/core/      — сервисы и конфигурация
    config/app.config.ts   — провайдеры бутстрапа (роутер, локаль, PrimeNG)
    config/app.routes.ts   — маршруты (наполняется задачами фич)
    config/primeng-ru.ts   — русские переводы PrimeNG
src/app/mock/      — мок-слой (наполняется задачами мок-БД)
src/app/shared/    — валидаторы, модели, компоненты, переиспользуемые между фичами
src/app/layout/    — оболочка приложения и страница 403
    app-root/              — корневой компонент (точка входа бутстрапа)
src/app/features/  — фичи: features/<фича>/pages, features/<фича>/components
src/styles.scss    — глобальные стили
src/styles/        — SCSS-примеси: единственный брейкпоинт 768px
```

`src/main.ts`, `src/index.html` и `src/styles.scss` — стандартные точки
входа Angular CLI вне `src/app`.
