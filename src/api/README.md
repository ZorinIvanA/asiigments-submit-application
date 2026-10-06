# API — бэкенд «Сдача лабораторных работ»

Здесь размещается серверная часть системы: монолитное приложение на
**ASP.NET Core** (.NET 10), которое обслуживает REST API `/api/v1` и раздаёт
статику собранного Angular-клиента из `src/client` (подробности —
[Спецификация системы](../../docs/system-specification.md), §2 «Стек и
архитектура»).

Ключевые положения спецификации:

- REST API с версионированием в пути: `/api/v1/...`
- EF Core 10 + PostgreSQL 16+ (Npgsql), code-first миграции
- Аутентификация: JWT в httpOnly-cookie (access 15 мин + refresh 7 дней)
- SPA fallback: все не-`/api` маршруты отдают `index.html`
- Health-check `GET /health`; Swagger UI только в dev
- Dev-режим: Angular dev-server (`src/client`) с прокси `/api` на Kestrel

До появления реального бэкенда клиент работает на браузерном мок-слое
(`src/client/app/mock`).

Структура каталога будет наполняться по мере разработки (лабораторная №3,
ветка `lab3-api`).
