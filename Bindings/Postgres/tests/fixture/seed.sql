-- Nissth Bridge — PostgreSQL binding fixture seed.
-- Idempotent: safe to re-run inside a fresh schema or per-test schema.
-- Schema:
--   users(id, name, email, created_at)
--   orders(id, user_id [FK users], amount, placed_at)
-- View:
--   user_order_count(user_id, name, n_orders)
-- Indexes:
--   idx_orders_user_id    — used (queries filter by user_id)
--   idx_orders_unused     — created but never queried (exercises index_audit --mode unused)

DROP VIEW   IF EXISTS user_order_count;
DROP TABLE  IF EXISTS orders;
DROP TABLE  IF EXISTS users;

CREATE TABLE users (
    id          BIGSERIAL PRIMARY KEY,
    name        TEXT NOT NULL,
    email       TEXT NOT NULL UNIQUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE orders (
    id          BIGSERIAL PRIMARY KEY,
    user_id     BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    amount      NUMERIC(10, 2) NOT NULL CHECK (amount > 0),
    placed_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_orders_user_id ON orders(user_id);
CREATE INDEX idx_orders_unused  ON orders(amount);

CREATE VIEW user_order_count AS
    SELECT u.id AS user_id, u.name, COUNT(o.id) AS n_orders
    FROM users u
    LEFT JOIN orders o ON o.user_id = u.id
    GROUP BY u.id, u.name;

INSERT INTO users (name, email) VALUES
    ('Alice', 'alice@example.com'),
    ('Bob',   'bob@example.com'),
    ('Carol', 'carol@example.com'),
    ('Dave',  'dave@example.com'),
    ('Eve',   'eve@example.com');

INSERT INTO orders (user_id, amount) VALUES
    (1, 19.99),
    (1, 49.50),
    (2, 12.00),
    (2,  5.00),
    (2, 99.99),
    (3,  7.25),
    (4, 14.00),
    (4, 14.00),
    (5, 21.75),
    (5, 33.40);

-- Trigger one read of idx_orders_user_id so pg_stat_user_indexes reflects usage.
SELECT * FROM orders WHERE user_id = 1;
