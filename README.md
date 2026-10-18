# Сдача лабораторных работ

Приложение для учёта сдачи лабораторных работ: Angular-клиент и бэкенд на
ASP.NET Core, работающие вместе по контракту REST API `/api/v1`. Клиент ходит
в API через `HttpClient` (мок-слой удалён); аутентификация — JWT в
httpOnly-cookie (access 15 мин + refresh 7 дней).

Репозиторий разделён на две части: `src/client` — Angular-приложение
(клиент), `src/api` — бэкенд (ASP.NET Core, .NET 8; подробности — в
[README](src/api/README.md) каталога и в
[спецификации](docs/system-specification.md)).

## Стек

Клиент:

- Angular 20+, standalone-компоненты, TypeScript strict
- SCSS, единственный брейкпоинт проекта — **768px** (граница мобильной вёрстки)
- PrimeNG 20+ с темой Aura (`@primeuix/themes`, только светлый режим) и primeicons
- Русская локаль: `LOCALE_ID = ru`, `registerLocaleData(localeRu)`, русские
  переводы встроенных текстов PrimeNG (`core/config/primeng-ru.ts`)
- Тесты: Karma + Jasmine (ChromeHeadless)

Бэкенд (`src/api`, .NET 8):

- REST API с версионированием в пути `/api/v1`, единый конверт ошибок
  `{message, errors?}`
- Аутентификация: JWT в httpOnly-cookie, PBKDF2-хэширование паролей,
  rate-limiting входа/восстановления
- Хранилище: in-memory реализации интерфейсов репозиториев (подготовлено к
  замене на реальную БД без изменения контроллеров)
- Health-check `GET /health`; Swagger UI — только в Development
- Тесты: xUnit — 1107 юнит-тестов (`src/api/LabsApp.Tests`) + 22
  интеграционные зоны (`tests/integration/B-01…B-22`, HTTP-контракты `/api/v1`
  через WebApplicationFactory)

## Установка и запуск

Требуется Node.js LTS (20.19+ или 22.12+) и npm, Google Chrome (для
unit-тестов в ChromeHeadless), .NET SDK 8.0.

### Режим разработки (два процесса)

```bash
dotnet run --project src/api/LabsApp     # бэкенд: http://localhost:5080
npm install                              # зависимости клиента (первый запуск)
npm start                                # клиент: http://localhost:4200
```

`ng serve` проксирует `/api` и `/health` на бэкенд (`src/client/proxy.conf.json`).
Приложение открывается по адресу **http://localhost:4200** (гость попадает на
экран входа; после входа — домашний экран по роли).

### Production-режим (один процесс)

Клиент собирается и раздаётся бэкендом из `wwwroot` (подробности — в
[README](src/api/README.md) каталога `src/api`):

```bash
npm run build
mkdir -p src/api/LabsApp/wwwroot
cp -r dist/asiigments-submit-application/browser/. src/api/LabsApp/wwwroot/
dotnet run --project src/api/LabsApp      # всё вместе: http://localhost:5080
```

Прочие команды клиента:

```bash
npm test           # тесты клиента (Karma + Jasmine, ChromeHeadless):
                   # основной проект + интеграционные TS-зоны B-17/B-18
npm run build      # production-сборка (ng build --configuration production)
npm run watch      # пересборка разработки при изменениях
```

Тесты бэкенда:

```bash
dotnet test src/api/LabsApp.sln                       # юнит-тесты бэкенда
dotnet test tests/integration/B-01                    # одна зона (пример)
# все зоны: tests/integration/B-01…B-22, по одной на батч кейсов
```

## Демо-учётки и данные

Данные бэкенда — in-memory: сид выполняется при старте, изменения живут до
перезапуска процесса (перезапуск = сброс к сиду). В Development «письма»
восстановления пароля не отправляются: код выводится в журнал бэкенда с
маркером `[DEV-EMAIL]` (запуск `dotnet run` показывает его в консоли).

### Демо-учётки

| Логин | Пароль | Роль | Группа |
| --- | --- | --- | --- |
| `teacher` | `teacher123!` | преподаватель | — |
| `studentNN`, NN = 01…32 | `student123!` | студент | 01–25 → ИК-221, 26–30 → ИК-222, 31–32 — без группы |

Логин/пароль преподавателя и включение демо-набора настраиваются переменными
`Seed__TeacherLogin`, `Seed__TeacherPassword`, `Seed__DemoData` (полная таблица
переменных — в [README](src/api/README.md) бэкенда).

Остальные данные сида: группы ИК-221/ИК-222/ИК-223 (у ИК-223 студентов нет),
23 лабораторные работы (семестр 1 — №1–20, семестр 2 — №1–3), сид-сдачи.

### Особенности демо-режима

- Тёмная тема фиксированно отключена (`darkModeSelector: false`) — макетами
  предусмотрена только светлая схема.
- В Production переменная `Seed__TeacherPassword` обязательна и не может
  совпадать с демо-значением (guard старта).

## Структура каталогов (NFR-003)

`src` состоит из двух частей: `src/client` — Angular-приложение (клиент),
`src/api` — бэкенд (ASP.NET Core .NET 8).

Внутри `src/client/app` код располагается только в четырёх ветках; иные
отклонения недопустимы. Файлы бутстрапа сознательно разложены по тем же веткам:
маршруты и конфигурация — это «конфиг» (core), корневой компонент — «оболочка»
(layout).

```
src/client/app/core/      — сервисы (HttpClient, /api/v1) и конфигурация
    config/app.config.ts   — провайдеры бутстрапа (роутер, локаль, PrimeNG)
    config/app.routes.ts   — маршруты
    config/primeng-ru.ts   — русские переводы PrimeNG
src/client/app/shared/    — валидаторы, модели, компоненты, переиспользуемые между фичами
src/client/app/layout/    — оболочка приложения и страница 403
    app-root/              — корневой компонент (точка входа бутстрапа)
src/client/app/features/  — фичи: features/<фича>/pages, features/<фича>/components
src/api/                  — бэкенд: ASP.NET Core, REST API /api/v1 (см. src/api/README.md)
tests/integration/        — интеграционные зоны B-01…B-22 (xUnit + TS-зоны B-17/B-18)
src/client/styles.scss    — глобальные стили
src/client/styles/        — SCSS-примеси: единственный брейкпоинт 768px
```

`src/client/main.ts`, `src/client/index.html` и `src/client/styles.scss` —
стандартные точки входа Angular CLI вне `src/client/app`.
