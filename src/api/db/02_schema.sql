-- ============================================================================
-- 02_schema.sql — схема данных приложения «Сдача лабораторных работ» (3НФ).
--
-- Запуск от роли-владельца (labsapp) ПОДКЛЮЧИВШИСЬ к базе labsapp:
--   psql -h 127.0.0.1 -U labsapp -d labsapp -f 02_schema.sql
--
-- Идемпотентно (IF NOT EXISTS). Полный снос и пересоздание — 99_drop_all.sql.
--
-- Нормализация (3НФ):
--  * каждая таблица — одна сущность, все неключевые атрибуты атомарны и зависят
--    только от ключа (uuid / token_hash); транзитивных зависимостей нет;
--  * имя группы НЕ хранится в users (только FK group_id) — денормализация
--    отсутствует, имя разрешается соединением;
--  * studentCount группы НЕ хранится — вычисляется подсчётом студентов;
--  * семестр сдачи НЕ хранится в submissions — выводится через lab_id.
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- Группы студентов.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS groups (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name        text        NOT NULL,
    created_at  timestamp with time zone NOT NULL DEFAULT now(),

    -- Полевые правила приложения (FR-019): 1–100 символов после трима.
    CONSTRAINT groups_name_length CHECK (char_length(btrim(name)) BETWEEN 1 AND 100)
);

-- ci-уникальность имени — правило коллации приложения (lower + ordinal);
-- приложение хранит имена уже триммированными.
CREATE UNIQUE INDEX IF NOT EXISTS groups_name_ci_key ON groups (lower(name));

-- ---------------------------------------------------------------------------
-- Пользователи: студенты и преподаватели.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    login         text        NOT NULL,
    email         text        NOT NULL,
    password_hash text        NOT NULL,
    full_name     text        NOT NULL,
    role          text        NOT NULL,
    group_id      uuid        REFERENCES groups (id) ON DELETE SET NULL,
    created_at    timestamp with time zone NOT NULL DEFAULT now(),

    CONSTRAINT users_role_check      CHECK (role IN ('student', 'teacher')),
    CONSTRAINT users_login_length    CHECK (char_length(login) BETWEEN 1 AND 100),
    CONSTRAINT users_email_length    CHECK (char_length(email) BETWEEN 1 AND 254),
    CONSTRAINT users_full_name_length CHECK (char_length(full_name) BETWEEN 1 AND 200)
);

-- ci-уникальность login и email — правило коллации приложения.
CREATE UNIQUE INDEX IF NOT EXISTS users_login_ci_key ON users (lower(login));
CREATE UNIQUE INDEX IF NOT EXISTS users_email_ci_key ON users (lower(email));

-- Пути доступа: состав группы, перечень студентов.
CREATE INDEX IF NOT EXISTS users_group_id_idx ON users (group_id) WHERE group_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS users_role_idx ON users (role);

-- ---------------------------------------------------------------------------
-- Лабораторные работы. Пара (семестр, номер) уникальна.
-- Верхнюю границу семестра проверяет приложение (Labs__MaxSemester —
-- конфигурация), БД гарантирует базовые инварианты.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS labs (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    semester         integer     NOT NULL,
    number           integer     NOT NULL,
    content          text        NOT NULL,
    assignment_url   text        NULL,
    defense_required boolean     NOT NULL DEFAULT false,
    created_at       timestamp with time zone NOT NULL DEFAULT now(),
    updated_at       timestamp with time zone NOT NULL DEFAULT now(),

    CONSTRAINT labs_semester_positive CHECK (semester >= 1),
    CONSTRAINT labs_number_positive   CHECK (number > 0),
    CONSTRAINT labs_content_length    CHECK (char_length(content) BETWEEN 1 AND 500),
    CONSTRAINT labs_url_length        CHECK (assignment_url IS NULL OR char_length(assignment_url) BETWEEN 1 AND 1000),
    CONSTRAINT labs_semester_number_key UNIQUE (semester, number)
);

-- ---------------------------------------------------------------------------
-- Сдачи: запись по уникальной паре (студент, работа). Обе даты NULL —
-- допустимое состояние «запись есть, отметок нет» (data_design FR-021).
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS submissions (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    student_id   uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    lab_id       uuid        NOT NULL REFERENCES labs (id) ON DELETE CASCADE,
    submit_date  date        NULL,
    defense_date date        NULL,
    updated_by   uuid        NULL REFERENCES users (id) ON DELETE SET NULL,
    updated_at   timestamp with time zone NOT NULL,

    CONSTRAINT submissions_student_lab_key UNIQUE (student_id, lab_id)
);

-- Пути доступа: каскад удаления работы, ведомость студента (префикс уника
-- уже покрывает student_id), «кто последний менял» не запрашивается по списку.
CREATE INDEX IF NOT EXISTS submissions_lab_id_idx ON submissions (lab_id);

COMMIT;
