-- ============================================================================
-- 03_seed_demo.sql — демо-данные (тот же набор, что и сид приложения SeedRunner).
--
-- Запуск от роли-владельца ПОСЛЕ 02_schema.sql:
--   psql -h 127.0.0.1 -U labsapp -d labsapp -f 03_seed_demo.sql
--
-- Идемпотентно: вставка только при отсутствии записи (ON CONFLICT DO NOTHING
-- по естественным ключам: логин/email, имя группы, пара (семестр, номер),
-- пара (студент, работа)).
--
-- Пароли (teacher123! / student123!) вшиты как готовые PBKDF2-хэши в формате
-- хэшера приложения «pbkdf2-sha256$итерации$соль$хэш» (210 000 итераций,
-- HMAC-SHA256): Verify читает параметры из самой строки, поэтому вход по этим
-- учёткам работает при любой конфигурации Auth__Pbkdf2Iterations.
--
-- UUID фиксированы, чтобы сид был воспроизводимым; приложение при своей работе
-- генерирует UUID заново и по естественным ключам не конфликтует.
-- ============================================================================

BEGIN;

-- Преподаватель (Seed__TeacherLogin по умолчанию).
INSERT INTO users (id, login, email, password_hash, full_name, role, group_id, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000101',
    'teacher',
    'teacher@example.com',
    'pbkdf2-sha256$210000$bT7JSBHmICLxTfULoOKy3Q==$bEVPkzxnLoNg/DyjNf8iHgBdG2zx4N9/Vg8GYRW1CWA=',
    'Сидоров Семён Семёнович',
    'teacher',
    NULL,
    '2026-09-01T08:00:00Z')
ON CONFLICT DO NOTHING;

-- Группы: ИК-221, ИК-222, ИК-223 (у ИК-223 студентов нет).
INSERT INTO groups (id, name, created_at)
VALUES
    ('00000000-0000-0000-0000-000000000201', 'ИК-221', '2026-09-01T08:00:00Z'),
    ('00000000-0000-0000-0000-000000000202', 'ИК-222', '2026-09-01T08:00:00Z'),
    ('00000000-0000-0000-0000-000000000203', 'ИК-223', '2026-09-01T08:00:00Z')
ON CONFLICT DO NOTHING;

-- Студенты student01..student32, пароль student123!:
-- 01–25 → ИК-221, 26–30 → ИК-222, 31–32 — без группы.
INSERT INTO users (id, login, email, password_hash, full_name, role, group_id, created_at)
SELECT
    ('00000000-0000-0000-0000-00000000' || lpad(nn::text, 4, '0'))::uuid,
    'student' || lpad(nn::text, 2, '0'),
    'student' || lpad(nn::text, 2, '0') || '@example.com',
    'pbkdf2-sha256$210000$9bmIv+UWplSZVxlvUh4x1g==$7OQI6KQXg62LgyTm8wWoCOd5OB7NheAnf1uOhLikHoQ=',
    'Иванов Иван Иванович ' || lpad(nn::text, 2, '0'),
    'student',
    CASE
        WHEN nn <= 25 THEN '00000000-0000-0000-0000-000000000201'::uuid
        WHEN nn <= 30 THEN '00000000-0000-0000-0000-000000000202'::uuid
        ELSE NULL
    END,
    '2026-09-01T08:00:00Z'
FROM generate_series(1, 32) AS nn
ON CONFLICT DO NOTHING;

-- Работы: семестр 1 №1–20 и семестр 2 №1–3; защита у чётных номеров,
-- ссылка на задание у кратных пяти (воспроизведение сида приложения).
INSERT INTO labs (id, semester, number, content, assignment_url, defense_required, created_at, updated_at)
SELECT
    ('00000000-0000-0000-0001-00000000' || lpad(((semester - 1) * 20 + number)::text, 4, '0'))::uuid,
    semester,
    number,
    'Содержание лабораторной работы №' || number,
    CASE WHEN number % 5 = 0
        THEN 'https://git.example.com/assignments/' || semester || '/' || number
        ELSE NULL
    END,
    number % 2 = 0,
    '2026-09-01T08:00:00Z',
    '2026-09-01T08:00:00Z'
FROM (VALUES (1, 20), (2, 3)) AS span(semester, last_number)
JOIN LATERAL generate_series(1, span.last_number) AS number ON true
ON CONFLICT DO NOTHING;

-- Сид-сдачи (student01/student02 × работы семестра 1); метки фиксированы
-- как и в сида приложения (2026-09-11T12:00:00Z, преподаватель).
INSERT INTO submissions (id, student_id, lab_id, submit_date, defense_date, updated_by, updated_at)
VALUES
    ('00000000-0000-0000-0002-000000000001',
     '00000000-0000-0000-0000-000000000001',  -- student01
     '00000000-0000-0000-0001-000000000001',  -- работа (1, 1)
     DATE '2026-09-01', DATE '2026-09-11',
     '00000000-0000-0000-0000-000000000101', TIMESTAMPTZ '2026-09-11 12:00:00Z'),
    ('00000000-0000-0000-0002-000000000002',
     '00000000-0000-0000-0000-000000000001',  -- student01
     '00000000-0000-0000-0001-000000000002',  -- работа (1, 2)
     DATE '2026-09-02', DATE '2026-09-12',
     '00000000-0000-0000-0000-000000000101', TIMESTAMPTZ '2026-09-11 12:00:00Z'),
    ('00000000-0000-0000-0002-000000000003',
     '00000000-0000-0000-0000-000000000001',  -- student01
     '00000000-0000-0000-0001-000000000003',  -- работа (1, 3)
     DATE '2026-09-03', NULL,
     '00000000-0000-0000-0000-000000000101', TIMESTAMPTZ '2026-09-11 12:00:00Z'),
    ('00000000-0000-0000-0002-000000000004',
     '00000000-0000-0000-0000-000000000002',  -- student02
     '00000000-0000-0000-0001-000000000001',  -- работа (1, 1)
     DATE '2026-09-01', NULL,
     '00000000-0000-0000-0000-000000000101', TIMESTAMPTZ '2026-09-11 12:00:00Z')
ON CONFLICT DO NOTHING;

COMMIT;
