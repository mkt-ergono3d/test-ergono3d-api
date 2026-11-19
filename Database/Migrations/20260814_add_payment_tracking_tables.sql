-- IBI-42: additive migration only. This script does not alter existing tables or data.
CREATE TABLE IF NOT EXISTS payment_attempt (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    product_type varchar(32) NOT NULL,
    source_page varchar(512) NOT NULL,
    cta_placement varchar(128) NOT NULL,
    language varchar(32) NOT NULL,
    design_type varchar(128) NULL,
    return_path varchar(1024) NOT NULL,
    status varchar(32) NOT NULL,
    stripe_checkout_session_id varchar(255) NULL,
    stripe_payment_intent_id varchar(255) NULL,
    amount bigint NULL,
    currency varchar(12) NULL,
    failure_code varchar(128) NULL,
    created_time timestamptz NOT NULL,
    updated_time timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_attempt_checkout_session
    ON payment_attempt (stripe_checkout_session_id)
    WHERE stripe_checkout_session_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_attempt_payment_intent
    ON payment_attempt (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_payment_attempt_user_id
    ON payment_attempt (user_id);

CREATE TABLE IF NOT EXISTS stripe_webhook_event (
    stripe_event_id varchar(255) PRIMARY KEY,
    event_type varchar(128) NOT NULL,
    payment_attempt_id uuid NULL,
    processed_time timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_stripe_webhook_event_payment_attempt_id
    ON stripe_webhook_event (payment_attempt_id);
