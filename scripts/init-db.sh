#!/bin/bash
set -e

# POSTGRES_HOST unset: runs inside the db container via
# /docker-entrypoint-initdb.d (local socket). Set (db-ensure one-shot):
# connects over TCP, so upgraded volumes get the same prerequisites.
PSQL_HOST=()
if [ -n "${POSTGRES_HOST:-}" ]; then
  PSQL_HOST=(-h "$POSTGRES_HOST")
  # psql authenticates over TCP via PGPASSWORD, not POSTGRES_PASSWORD.
  export PGPASSWORD="${POSTGRES_PASSWORD:-}"
fi

psql -v ON_ERROR_STOP=1 "${PSQL_HOST[@]}" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS vector;
EOSQL
echo "pgvector extension enabled."

# Judge0 connects with POSTGRES_DB=judge0; the postgres entrypoint only
# creates POSTGRES_DB on a fresh volume, so create it here too (idempotent).
if [ "$(psql "${PSQL_HOST[@]}" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -tAc "SELECT 1 FROM pg_database WHERE datname='judge0'")" != "1" ]; then
    psql "${PSQL_HOST[@]}" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "CREATE DATABASE judge0;"
    echo "judge0 database created."
else
    echo "judge0 database already present."
fi
