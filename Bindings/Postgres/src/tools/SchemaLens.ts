import type { Client } from "pg";
import { ConnectionManager } from "../core/ConnectionManager";
import { ReportWriter } from "../core/ReportWriter";
import { StaleFlipper } from "../core/StaleFlipper";
import { findRepoRoot } from "../core/repoRoot";
import type { BridgeCommand, ToolHandler, ToolResult } from "../core/types";

interface TableRow {
  table_schema: string;
  table_name: string;
  table_type: string;
}

interface ColumnRow {
  table_name: string;
  column_name: string;
  ordinal_position: number;
  data_type: string;
  is_nullable: string;
  column_default: string | null;
}

interface FkRow {
  constraint_name: string;
  table_schema: string;
  table_name: string;
  column_name: string;
  foreign_table_schema: string;
  foreign_table_name: string;
  foreign_column_name: string;
}

type Mode = "tables" | "columns" | "relationships" | "full";

export class SchemaLens implements ToolHandler {
  public readonly name = "schema_lens";

  constructor(private readonly bindingVersion: string) {}

  async invoke(cmd: BridgeCommand): Promise<ToolResult> {
    const mode = (cmd.mode ?? "full") as Mode;
    if (!["tables", "columns", "relationships", "full"].includes(mode)) {
      throw new Error(`schema_lens: unknown mode '${mode}'`);
    }
    const schema = cmd.scope?.package ?? "public";
    const typeFilter = cmd.scope?.type_filter ?? "BASE TABLE";
    const nameAllowlist = cmd.scope?.names;

    const result = await ConnectionManager.withClient(cmd, async (client, parsed) => {
      const fingerprint = await ConnectionManager.fingerprint(client);

      const tables = (mode === "tables" || mode === "full" || mode === "columns" || mode === "relationships")
        ? await SchemaLens.queryTables(client, schema, typeFilter, nameAllowlist)
        : [];
      const columns = (mode === "columns" || mode === "full")
        ? await SchemaLens.queryColumns(client, schema, tables.map((t) => t.table_name))
        : [];
      const relationships = (mode === "relationships" || mode === "full")
        ? await SchemaLens.queryRelationships(client, schema, tables.map((t) => t.table_name))
        : [];

      const body = SchemaLens.renderBody(mode, schema, tables, columns, relationships, parsed);

      const repoRoot = findRepoRoot();
      const writer = new ReportWriter({
        binding: "postgres",
        bindingVersion: this.bindingVersion,
        repoRoot,
      });
      const reportPath = writer.write({
        tool: "schema_lens",
        mode,
        scope: SchemaLens.scrubScope(cmd.scope),
        freshness: {
          source: ConnectionManager.redactedUrl(parsed),
          source_state: `redo_lsn=${fingerprint}`,
          guarantee: `Fresh PG connection per call; redo_lsn captured at run start; statement_timeout=${ConnectionManager.resolveTimeout(cmd)}ms; password redacted via ConnectionManager.redactForLog().`,
        },
        body,
      });

      // STALE-flip DBL/SchemaIndex/*.md when live table set diverges from documented.
      const reportFileName = reportPath.split(/[/\\]/).pop() ?? "";
      const flipper = new StaleFlipper(repoRoot);
      const liveTableNames = new Set(tables.map((t) => `${schema}.${t.table_name}`));
      const flippedSchema = flipper.flipIfStale({
        dblSubdir: "SchemaIndex",
        scopePath: schema,
        reportFileName,
        driftCheck: (fm, _body) => {
          const documented = SchemaLens.extractDocumentedTables(fm);
          if (documented.length === 0) return false;
          const documentedSet = new Set(documented);
          // drift if any live table is missing from docs OR any documented table is missing from live
          for (const t of liveTableNames) if (!documentedSet.has(t)) return true;
          for (const t of documentedSet) if (!liveTableNames.has(t)) return true;
          return false;
        },
      });
      // Also flip DependencyMaps/*.md when FK graph diverges.
      const liveFkSet = new Set(
        relationships.map((r) => `${r.table_name}.${r.column_name}->${r.foreign_table_name}.${r.foreign_column_name}`)
      );
      const flippedDepMaps = flipper.flipIfStale({
        dblSubdir: "DependencyMaps",
        scopePath: schema,
        reportFileName,
        driftCheck: (fm, _body) => {
          const documented = SchemaLens.extractDocumentedFks(fm);
          if (documented.length === 0) return false;
          const documentedSet = new Set(documented);
          for (const f of liveFkSet) if (!documentedSet.has(f)) return true;
          for (const f of documentedSet) if (!liveFkSet.has(f)) return true;
          return false;
        },
      });

      return { reportPath, flipped: [...flippedSchema, ...flippedDepMaps] };
    });

    return { reportPath: result.reportPath };
  }

  private static async queryTables(
    client: Client,
    schema: string,
    typeFilter: string,
    names: string[] | undefined
  ): Promise<TableRow[]> {
    const params: unknown[] = [schema, typeFilter];
    let sql = `
      SELECT table_schema, table_name, table_type
      FROM information_schema.tables
      WHERE table_schema = $1 AND table_type = $2
    `;
    if (names && names.length > 0) {
      params.push(names);
      sql += ` AND table_name = ANY($3::text[])`;
    }
    sql += ` ORDER BY table_name`;
    const r = await client.query<TableRow>(sql, params);
    return r.rows;
  }

  private static async queryColumns(client: Client, schema: string, tableNames: string[]): Promise<ColumnRow[]> {
    if (tableNames.length === 0) return [];
    const r = await client.query<ColumnRow>(
      `
        SELECT table_name, column_name, ordinal_position,
               data_type, is_nullable, column_default
        FROM information_schema.columns
        WHERE table_schema = $1 AND table_name = ANY($2::text[])
        ORDER BY table_name, ordinal_position
      `,
      [schema, tableNames]
    );
    return r.rows;
  }

  private static async queryRelationships(client: Client, schema: string, tableNames: string[]): Promise<FkRow[]> {
    if (tableNames.length === 0) return [];
    const r = await client.query<FkRow>(
      `
        SELECT
          rc.constraint_name,
          kcu.table_schema, kcu.table_name, kcu.column_name,
          ccu.table_schema AS foreign_table_schema,
          ccu.table_name   AS foreign_table_name,
          ccu.column_name  AS foreign_column_name
        FROM information_schema.referential_constraints rc
        JOIN information_schema.key_column_usage kcu
          ON kcu.constraint_name = rc.constraint_name
         AND kcu.constraint_schema = rc.constraint_schema
        JOIN information_schema.constraint_column_usage ccu
          ON ccu.constraint_name = rc.unique_constraint_name
         AND ccu.constraint_schema = rc.unique_constraint_schema
        WHERE kcu.table_schema = $1 AND kcu.table_name = ANY($2::text[])
        ORDER BY kcu.table_name, kcu.column_name
      `,
      [schema, tableNames]
    );
    return r.rows;
  }

  private static renderBody(
    mode: Mode,
    schema: string,
    tables: TableRow[],
    columns: ColumnRow[],
    relationships: FkRow[],
    parsed: { host: string; port: number; database: string; user: string }
  ): string {
    const parts: string[] = [];
    parts.push(`# schema_lens — ${schema} @ ${parsed.host}:${parsed.port}/${parsed.database}`);
    parts.push(`> User: \`${parsed.user}\` · Schema: \`${schema}\` · Mode: \`${mode}\``);
    parts.push("");
    if (mode === "tables" || mode === "full" || mode === "columns" || mode === "relationships") {
      parts.push(`## Tables (${tables.length})`);
      if (tables.length === 0) {
        parts.push("_No tables match the filter._");
      } else {
        parts.push("| Schema | Name | Type |");
        parts.push("|:---|:---|:---|");
        for (const t of tables) {
          parts.push(`| ${t.table_schema} | ${t.table_name} | ${t.table_type} |`);
        }
      }
      parts.push("");
    }
    if (mode === "columns" || mode === "full") {
      parts.push(`## Columns (${columns.length})`);
      if (columns.length === 0) {
        parts.push("_No columns in scope._");
      } else {
        parts.push("| Table | # | Column | Type | Nullable | Default |");
        parts.push("|:---|:---|:---|:---|:---|:---|");
        for (const c of columns) {
          const def = c.column_default ?? "";
          parts.push(`| ${c.table_name} | ${c.ordinal_position} | ${c.column_name} | ${c.data_type} | ${c.is_nullable} | ${def} |`);
        }
      }
      parts.push("");
    }
    if (mode === "relationships" || mode === "full") {
      parts.push(`## Foreign-key relationships (${relationships.length})`);
      if (relationships.length === 0) {
        parts.push("_No FKs in scope._");
      } else {
        parts.push("| Constraint | From | -> | To |");
        parts.push("|:---|:---|:---|:---|");
        for (const r of relationships) {
          parts.push(`| ${r.constraint_name} | ${r.table_name}.${r.column_name} | → | ${r.foreign_table_name}.${r.foreign_column_name} |`);
        }
      }
      parts.push("");
    }
    return parts.join("\n");
  }

  private static scrubScope(scope: BridgeCommand["scope"]): Record<string, unknown> | undefined {
    if (!scope) return undefined;
    const out: Record<string, unknown> = { ...scope };
    if (scope.extra) {
      const extraOut: Record<string, unknown> = { ...scope.extra };
      if ("connection_string" in extraOut) {
        extraOut["connection_string"] = "***REDACTED***";
      }
      out["extra"] = extraOut;
    }
    return out;
  }

  /**
   * Extract documented tables from a DBL/SchemaIndex artifact's frontmatter.
   * Looks for a `tables:` list of "<schema>.<name>" strings.
   */
  static extractDocumentedTables(fm: Record<string, unknown>): string[] {
    const v = fm["tables"];
    if (Array.isArray(v)) return v.filter((x): x is string => typeof x === "string");
    return [];
  }

  /**
   * Extract documented FKs from a DBL/DependencyMaps artifact's frontmatter.
   * Looks for a `foreign_keys:` list of "<table>.<col>-><ftable>.<fcol>" strings.
   */
  static extractDocumentedFks(fm: Record<string, unknown>): string[] {
    const v = fm["foreign_keys"];
    if (Array.isArray(v)) return v.filter((x): x is string => typeof x === "string");
    return [];
  }
}
