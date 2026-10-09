#!/bin/sh
set -eu

: "${GYMSHOP_APP_DB_PASSWORD:?GYMSHOP_APP_DB_PASSWORD is required}"
: "${GYMSHOP_MIGRATION_DB_PASSWORD:?GYMSHOP_MIGRATION_DB_PASSWORD is required}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=app_password="$GYMSHOP_APP_DB_PASSWORD" \
  --set=migration_password="$GYMSHOP_MIGRATION_DB_PASSWORD" <<'SQL'
DO $roles$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gymshop_app') THEN
    CREATE ROLE gymshop_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gymshop_migrator') THEN
    CREATE ROLE gymshop_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
  END IF;
END
$roles$;

ALTER ROLE gymshop_app PASSWORD :'app_password';
ALTER ROLE gymshop_migrator PASSWORD :'migration_password';
ALTER DATABASE "GymShopDb" OWNER TO gymshop_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO gymshop_app;
GRANT USAGE, CREATE ON SCHEMA public TO gymshop_migrator;
ALTER DEFAULT PRIVILEGES FOR ROLE gymshop_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gymshop_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gymshop_migrator IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO gymshop_app;
SQL
