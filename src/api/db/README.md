# База данных приложения (PostgreSQL 16)

Схема данных «Сдача лабораторных работ» в третьей нормальной форме (3НФ) и
демо-набор данных. Скрипты — источник истины схемы: бэкенд (EF Core + Npgsql)
маппируется на готовые таблицы и схему сам не создаёт и не мигрирует.

## Состав

| Файл | Назначение | Под кем запускать |
| --- | --- | --- |
| `01_create_database.sql` | роль `labsapp` и база `labsapp` | суперпользователь (`postgres`) |
| `02_schema.sql` | 7 таблиц, PK/FK/CHECK, уникальные и функциональные ci-индексы | `labsapp` (в базе `labsapp`) |
| `03_seed_demo.sql` | демо-набор (teacher, 3 группы, 32 студента, 23 работы, 4 сдачи) с PBKDF2-хэшами паролей | `labsapp` |
| `99_drop_all.sql` | полный снос базы и роли для чистого пересоздания | суперпользователь |
| `docker-compose.yml` | опциональный PostgreSQL 16, если нет локального сервера | — |

## Применение

### Вариант А — пользовательский кластер без root (использован на этой машине)

Системные кластеры требуют прав root/sudo, поэтому для разработки поднят
пользовательский кластер в `~/labsapp-pg` на порту **5434** (superuser
`labsapp/labsapp`, сокет в каталоге данных):

```bash
# первичная инициализация (однократно)
/usr/lib/postgresql/16/bin/initdb -D ~/labsapp-pg -U labsapp --auth=md5 \
  --pwfile=<(echo labsapp) -E UTF8
# старт кластера
/usr/lib/postgresql/16/bin/pg_ctl -D ~/labsapp-pg -l ~/labsapp-pg/server.log \
  -o '-p 5434 -c listen_addresses=127.0.0.1 -k /home/ivan/labsapp-pg' start

# применение скриптов (лабы: роль и база уже есть — 01 можно пропустить,
# этот кластер создан сразу под владельцем labsapp)
psql -h 127.0.0.1 -p 5434 -U labsapp -d labsapp -f 02_schema.sql
psql -h 127.0.0.1 -p 5434 -U labsapp -d labsapp -f 03_seed_demo.sql

# остановка кластера
/usr/lib/postgresql/16/bin/pg_ctl -D ~/labsapp-pg stop
```

Строка подключения: `Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp`.

### Вариант Б — системный сервер с правами администратора

```bash
psql -U postgres -f 01_create_database.sql
psql -h 127.0.0.1 -U labsapp -d labsapp -f 02_schema.sql
psql -h 127.0.0.1 -U labsapp -d labsapp -f 03_seed_demo.sql
```

### Вариант В — Docker

`docker compose up -d` создаёт пользователя и базу сам — начинайте сразу
с `02_schema.sql` (подключение `labsapp/labsapp@127.0.0.1:5432/labsapp`).

Снос и пересоздание с нуля (для варианта Б/В):

```bash
psql -U postgres -f 99_drop_all.sql
# … затем снова 01 → 02 → 03
```

Все скрипты идемпотентны: повторный запуск ничего не дублирует.

## Подключение бэкенда

Переменные окружения API (конфигурация бэкенда — только через переменные,
см. `src/api/README.md`):

```
Storage__Provider=postgres                                                    # postgres (по умолчанию) | inmemory
Storage__ConnectionString=Host=127.0.0.1;Port=5434;Database=labsapp;Username=labsapp;Password=labsapp
```

(порт — по выбранному варианту развёртывания: 5434 — пользовательский кластер,
5432 — docker-compose)

## Демо-учётки

| Логин | Пароль | Роль |
| --- | --- | --- |
| `teacher` | `teacher123!` | преподаватель |
| `student01`…`student32` | `student123!` | студент (01–25 → ИК-221, 26–30 → ИК-222, 31–32 без группы) |

Пароли в SQL-сиде вшиты как готовые хэши формата приложения
`pbkdf2-sha256$итерации$соль$хэш` (210 000 итераций, HMAC-SHA256) — приложение
проверяет их штатным `IPasswordHasher`, параметры читаются из самой строки.

## Нормализация (3НФ)

- Атомарные значения и зависимость только от ключа: `groups`, `users`, `labs`,
  `submissions`, `refresh_tokens`, `recovery_codes`, `password_reset_tokens` —
  каждая неключевая колонка зависит исключительно от первичного ключа своей
  таблицы (`uuid`, у reset-токенов — `token_hash`).
- Без транзитивных зависимостей: имя группы не хранится в `users` (только
  `group_id` → соединение), семестр сдачи не хранится в `submissions`
  (выводится через `lab_id`).
- Без производных данных: `studentCount` группы нигде не хранится —
  вычисляется подсчётом студентов.
- Ci-уникальность `login`/`email`/имени группы — функциональные уникальные
  индексы `ON lower(колонка)` (правило коллации приложения: нижний регистр +
  ordinal).
- Каскады БД повторяют домен: удаление группы сбрасывает `users.group_id`
  (`ON DELETE SET NULL`), удаление работы удаляет её сдачи
  (`ON DELETE CASCADE`), токены пользователя удаляются вместе с учётной
  записью (`ON DELETE CASCADE`).

Схема связей (Mermaid) — в корневом `README.md` проекта.
