import { ConnectionManager } from "../../src/core/ConnectionManager";

/**
 * Phase 09.7 regression guard for the `coerceSsl` object-form bug.
 *
 * Before Phase 09.7, `coerceSsl` only matched `boolean` and seven specific
 * lowercase strings; any object input (including `{}` and
 * `{rejectUnauthorized: false}` — the exact shapes pg-connection-string
 * returns for `?sslmode=require`, `verify-full`, `no-verify`, and
 * `uselibpqcompat=true&sslmode=require`) fell through to `return undefined`.
 * That meant `withClient` never set `config.ssl`, the binding connected in
 * plaintext, and TLS-required hosts (Render, AWS RDS with rds.force_ssl,
 * Heroku, Supabase) reset the connection.
 *
 * The four "object form pass-through" cases below would have failed RED
 * on pre-fix code; they pass GREEN on post-fix code.
 */
describe("ConnectionManager.coerceSsl", () => {
  describe("boolean inputs", () => {
    it("returns true unchanged", () => {
      expect(ConnectionManager.coerceSsl(true)).toBe(true);
    });

    it("returns false unchanged", () => {
      expect(ConnectionManager.coerceSsl(false)).toBe(false);
    });
  });

  describe("string inputs (libpq sslmode strings)", () => {
    it.each([
      "require",
      "prefer",
      "allow",
      "disable",
      "verify-ca",
      "verify-full",
    ])("returns %p unchanged", (mode) => {
      expect(ConnectionManager.coerceSsl(mode)).toBe(mode);
    });

    it("normalizes uppercase to lowercase", () => {
      expect(ConnectionManager.coerceSsl("REQUIRE")).toBe("require");
    });

    it("returns undefined for unknown strings", () => {
      expect(ConnectionManager.coerceSsl("not-a-real-sslmode")).toBeUndefined();
    });
  });

  describe("object inputs (pg-connection-string output shape)", () => {
    it("returns empty object {} unchanged — the ?sslmode=require / verify-full shape", () => {
      // Pre-Phase 09.7: this returned undefined — the bug.
      expect(ConnectionManager.coerceSsl({})).toEqual({});
    });

    it("returns {rejectUnauthorized:false} unchanged — the ?sslmode=no-verify shape", () => {
      // Pre-Phase 09.7: this returned undefined — the bug.
      const obj = { rejectUnauthorized: false };
      expect(ConnectionManager.coerceSsl(obj)).toEqual({ rejectUnauthorized: false });
    });

    it("returns {rejectUnauthorized:true} unchanged", () => {
      const obj = { rejectUnauthorized: true };
      expect(ConnectionManager.coerceSsl(obj)).toEqual({ rejectUnauthorized: true });
    });

    it("preserves additional ssl-config keys (e.g., ca, cert, key)", () => {
      const obj = { rejectUnauthorized: false, ca: "fake-ca-pem", servername: "example.com" };
      const out = ConnectionManager.coerceSsl(obj);
      expect(out).toEqual(obj);
    });
  });

  describe("non-coercible inputs", () => {
    it("returns undefined for null", () => {
      expect(ConnectionManager.coerceSsl(null)).toBeUndefined();
    });

    it("returns undefined for undefined", () => {
      expect(ConnectionManager.coerceSsl(undefined)).toBeUndefined();
    });

    it("returns undefined for numbers", () => {
      expect(ConnectionManager.coerceSsl(42)).toBeUndefined();
    });

    it("returns undefined for symbols", () => {
      expect(ConnectionManager.coerceSsl(Symbol("nope"))).toBeUndefined();
    });
  });
});

/**
 * End-to-end check against the actual pg-connection-string output for every
 * URL form the UniHub-Backend session tried. These would all flip RED on
 * pre-fix code (parse(...).ssl ends up undefined after coerceSsl) — they
 * flip GREEN post-fix because the object branch survives.
 */
describe("ConnectionManager.parse — ssl field for libpq URL forms", () => {
  const base = "postgresql://u:p@h.example.com:5432/db";

  it("bare URL → ssl is undefined", () => {
    const parsed = ConnectionManager.parse(base, "test");
    expect(parsed.ssl).toBeUndefined();
  });

  it("?sslmode=require → ssl is {} (empty object)", () => {
    const parsed = ConnectionManager.parse(`${base}?sslmode=require`, "test");
    expect(parsed.ssl).toEqual({});
  });

  it("?sslmode=verify-full → ssl is {} (empty object)", () => {
    const parsed = ConnectionManager.parse(`${base}?sslmode=verify-full`, "test");
    expect(parsed.ssl).toEqual({});
  });

  it("?sslmode=no-verify → ssl is {rejectUnauthorized: false}", () => {
    const parsed = ConnectionManager.parse(`${base}?sslmode=no-verify`, "test");
    expect(parsed.ssl).toEqual({ rejectUnauthorized: false });
  });

  it("?sslmode=disable → ssl is false", () => {
    const parsed = ConnectionManager.parse(`${base}?sslmode=disable`, "test");
    expect(parsed.ssl).toBe(false);
  });

  it("?uselibpqcompat=true&sslmode=require → ssl is {rejectUnauthorized: false}", () => {
    const parsed = ConnectionManager.parse(`${base}?uselibpqcompat=true&sslmode=require`, "test");
    expect(parsed.ssl).toEqual({ rejectUnauthorized: false });
  });
});
