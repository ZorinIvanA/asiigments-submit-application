-- ============================================================================
-- 99_drop_all.sql — полный снос (база, роль). Запуск ПОД СУПЕРПОЛЬЗОВАТЕЛЕМ:
--   psql -U postgres -f 99_drop_all.sql
--
-- Разрывает активные подключения к базе labsapp, удаляет её и роль приложения.
-- После этого весь цикл можно повторить с 01_create_database.sql.
-- ============================================================================

SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = 'labsapp' AND pid <> pg_backend_pid();

DROP DATABASE IF EXISTS labsapp;
DROP ROLE IF EXISTS labsapp;
