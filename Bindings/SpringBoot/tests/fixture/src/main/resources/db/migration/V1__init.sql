CREATE TABLE items (
    id    BIGSERIAL PRIMARY KEY,
    name  VARCHAR(255) NOT NULL,
    qty   INTEGER      NOT NULL
);

CREATE INDEX idx_items_name ON items (name);
