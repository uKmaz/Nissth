import { QueryPlan } from "../../src/tools/QueryPlan";

describe("QueryPlan.isMutating", () => {
  it.each([
    "INSERT INTO users VALUES (1)",
    "  update users set name = 'x'",
    "DELETE FROM users",
    "TRUNCATE users",
    "CREATE TABLE t (a int)",
    "DROP TABLE t",
    "ALTER TABLE t ADD COLUMN c int",
    "GRANT SELECT ON t TO u",
    "REVOKE SELECT ON t FROM u",
    "COPY t FROM stdin",
    "VACUUM ANALYZE t",
    "CLUSTER t",
    "REINDEX TABLE t",
  ])("flags mutating: %s", (sql) => {
    expect(QueryPlan.isMutating(sql)).toBe(true);
  });

  it.each([
    "SELECT * FROM users",
    "  select 1",
    "WITH cte AS (SELECT 1) SELECT * FROM cte",
    "SHOW search_path",
    "EXPLAIN SELECT * FROM users",
    "VALUES (1, 2)",
  ])("treats as read-only: %s", (sql) => {
    expect(QueryPlan.isMutating(sql)).toBe(false);
  });
});

describe("QueryPlan.explainPrefix", () => {
  it("returns the right prefix per mode", () => {
    expect(QueryPlan.explainPrefix("explain")).toBe("EXPLAIN (FORMAT JSON)");
    expect(QueryPlan.explainPrefix("analyze")).toBe("EXPLAIN (ANALYZE, FORMAT JSON)");
    expect(QueryPlan.explainPrefix("buffers")).toBe("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)");
  });
});
