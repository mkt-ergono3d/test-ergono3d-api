-- Run this once before deploying the API version that writes generation_usage_daily.
-- The backfill uses the existing user-generation log. Internal health checks never
-- write to "record", so they are not included.

BEGIN;

CREATE TABLE IF NOT EXISTS generation_usage_daily (
    day_utc date PRIMARY KEY,
    user_generation_count bigint NOT NULL DEFAULT 0 CHECK (user_generation_count >= 0),
    failed_check_retry_count bigint NOT NULL DEFAULT 0 CHECK (failed_check_retry_count >= 0),
    created_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Safe to re-run on databases where the table was created before retry counts existed.
ALTER TABLE generation_usage_daily
    ADD COLUMN IF NOT EXISTS failed_check_retry_count bigint NOT NULL DEFAULT 0
    CHECK (failed_check_retry_count >= 0);

-- One-time historical backfill. The time column is already stored as UTC by the API.
INSERT INTO generation_usage_daily (day_utc, user_generation_count, created_at_utc, updated_at_utc)
SELECT time::date, COUNT(*), CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM "record"
GROUP BY time::date
ON CONFLICT (day_utc) DO NOTHING;

COMMIT;
