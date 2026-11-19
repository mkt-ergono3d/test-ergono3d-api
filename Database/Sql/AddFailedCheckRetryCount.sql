-- Run this once in pgAdmin 4 before deploying post-failure retry-count tracking.

BEGIN;

ALTER TABLE generation_usage_daily
    ADD COLUMN IF NOT EXISTS failed_check_retry_count bigint NOT NULL DEFAULT 0
    CHECK (failed_check_retry_count >= 0);

COMMIT;
