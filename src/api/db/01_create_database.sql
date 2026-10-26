-- ============================================================================
-- 01_create_database.sql — роль и база данных приложения.
--
-- Запуск ПОД СУПЕРПОЛЬЗОВАТЕЛЕМ (обычно postgres), подключение к обслуживаемой
-- БД по умолчанию (postgres):
--   psql -U postgres -f 01_create_database.sql
--
-- Идемпотентно: роль и база создаются только при отсутствии.
-- ============================================================================

-- Роль приложения (владелец схемы): минимальные права, без суперпользователя.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'labsapp') THEN
        CREATE ROLE labsapp LOGIN PASSWORD 'labsapp';
    END IF;
END
$$;

-- База данных приложения. CREATE DATABASE не выполняется внутри транзакции/
-- DO-блока, поэтому используется условная генерация команды через \gexec (psql).
SELECT 'CREATE DATABASE labsapp OWNER labsapp'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'labsapp')\gexec

-- Права по умолчанию: владелец labsapp уже имеет полный доступ к своей базе.
