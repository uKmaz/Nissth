-- Synthetic Flyway history table for migration_status IT.
-- Mirrors Flyway 9.x schema exactly; one applied row + one failed row for coverage.

DROP TABLE IF EXISTS flyway_schema_history;

CREATE TABLE flyway_schema_history (
    installed_rank   INTEGER NOT NULL,
    version          VARCHAR(50),
    description      VARCHAR(200) NOT NULL,
    type             VARCHAR(20) NOT NULL,
    script           VARCHAR(1000) NOT NULL,
    checksum         INTEGER,
    installed_by     VARCHAR(100) NOT NULL,
    installed_on     TIMESTAMP NOT NULL DEFAULT now(),
    execution_time   INTEGER NOT NULL,
    success          BOOLEAN NOT NULL,
    PRIMARY KEY (installed_rank)
);

CREATE INDEX flyway_schema_history_s_idx ON flyway_schema_history(success);

INSERT INTO flyway_schema_history
    (installed_rank, version, description, type, script, checksum, installed_by, execution_time, success)
VALUES
    (1, '1',     'baseline',           'SQL', 'V1__baseline.sql',           111, 'nissth', 42, TRUE),
    (2, '2',     'add user email idx', 'SQL', 'V2__add_user_email_idx.sql', 222, 'nissth', 18, TRUE),
    (3, '3.001', 'add orders index',   'SQL', 'V3_001__add_orders_idx.sql', 333, 'nissth',  7, FALSE);
