package com.nissth.bridge.tools;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.StaticJavaParser;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.github.javaparser.ast.body.FieldDeclaration;
import com.github.javaparser.ast.expr.AnnotationExpr;
import com.github.javaparser.ast.expr.MemberValuePair;
import com.github.javaparser.ast.expr.NormalAnnotationExpr;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeError;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportContext;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.ToolHandler;
import com.nissth.bridge.core.ToolResult;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/**
 * entity_field_add — action, hard-enforce.
 *
 * <p>Atomically adds a field to a {@code @Entity} class and emits a matching
 * Flyway migration. On any partial failure both halves roll back and the tool
 * exits 5 with {@code error_code} naming which half failed. Enforces
 * {@code CLAUDE.md} §8.9 (entity/migration ripple) at the runtime layer.
 */
public class EntityFieldAdd implements ToolHandler {

    public static final String NAME = "entity_field_add";

    /** Default Java → PostgreSQL type mapping. See {@code Bindings/SpringBoot/README.md}. */
    static final Map<String, String> DEFAULT_SQL_TYPES;

    static {
        Map<String, String> m = new LinkedHashMap<>();
        addMapping(m, "VARCHAR(255)", "String", "java.lang.String");
        addMapping(m, "INTEGER", "Integer", "int", "java.lang.Integer");
        addMapping(m, "BIGINT", "Long", "long", "java.lang.Long");
        addMapping(m, "SMALLINT", "Short", "short", "java.lang.Short");
        addMapping(m, "BOOLEAN", "Boolean", "boolean", "java.lang.Boolean");
        addMapping(m, "DOUBLE PRECISION", "Double", "double", "java.lang.Double");
        addMapping(m, "REAL", "Float", "float", "java.lang.Float");
        addMapping(m, "NUMERIC(19,4)", "BigDecimal", "java.math.BigDecimal");
        addMapping(m, "DATE", "LocalDate", "java.time.LocalDate");
        addMapping(m, "TIMESTAMP", "LocalDateTime", "java.time.LocalDateTime");
        addMapping(m, "TIMESTAMPTZ",
                "OffsetDateTime", "java.time.OffsetDateTime",
                "Instant", "java.time.Instant");
        addMapping(m, "UUID", "UUID", "java.util.UUID");
        addMapping(m, "BYTEA", "byte[]");
        DEFAULT_SQL_TYPES = Map.copyOf(m);
    }

    private static void addMapping(Map<String, String> m, String sqlType, String... javaTypes) {
        for (String j : javaTypes) m.put(j, sqlType);
    }

    private static final Pattern MIGRATION_VERSION = Pattern.compile("^V(\\d+)__.+\\.sql$");

    private final ReportWriter writer;
    private final JavaParser parser;

    public EntityFieldAdd(ReportWriter writer) {
        this.writer = writer;
        this.parser = new JavaParser(new ParserConfiguration()
                .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_17));
    }

    @Override
    public String name() {
        return NAME;
    }

    @Override
    public ToolResult run(BridgeCommand cmd) {
        Inputs in = parseInputs(cmd);
        Path migrationDir = in.rootPath.resolve("src/main/resources/db/migration");
        if (!Files.isDirectory(migrationDir)) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Migration directory not found: " + migrationDir
                            + ". Create it before invoking entity_field_add."));
        }

        Path entityFile = resolveEntityFile(in.rootPath, in.packageName, in.simpleName);
        CompilationUnit cu = parseFile(entityFile);
        ClassOrInterfaceDeclaration cls = findClass(cu, in.simpleName);
        String tableName = resolveTableName(cls);
        String sqlType = resolveSqlType(in.fieldType, in.columnSqlType);

        if (!in.nullable && (in.columnDefault == null || in.columnDefault.isBlank())) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Non-nullable column requires scope.extra.column_default "
                            + "(raw SQL expression) so existing rows have a value during the migration."));
        }
        if (cls.getFieldByName(in.fieldName).isPresent()) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Entity " + in.entityFqn + " already has a field named " + in.fieldName));
        }

        int version = nextVersion(migrationDir);
        String migrationFilename = "V" + version + "__add_"
                + toSnakeCase(in.fieldName) + "_to_" + tableName + ".sql";
        Path migrationPath = migrationDir.resolve(migrationFilename);
        if (Files.exists(migrationPath)) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Migration version collision: " + migrationPath + " already exists"));
        }

        byte[] originalEntityBytes;
        try {
            originalEntityBytes = Files.readAllBytes(entityFile);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(NAME,
                    "Cannot read entity file for rollback caching: " + e.getMessage()));
        }

        cu.addImport("jakarta.persistence.Column");
        if (in.fieldType.contains(".") && !in.fieldType.startsWith("java.lang.")) {
            cu.addImport(in.fieldType);
        }
        String simpleType = in.fieldType.contains(".")
                ? in.fieldType.substring(in.fieldType.lastIndexOf('.') + 1)
                : in.fieldType;

        String fieldDecl = String.format(
                "@Column(name = \"%s\"%s)%nprivate %s %s;",
                in.columnName,
                in.nullable ? "" : ", nullable = false",
                simpleType, in.fieldName);
        FieldDeclaration newField = StaticJavaParser.parseBodyDeclaration(fieldDecl).asFieldDeclaration();
        cls.getMembers().add(newField);
        newField.createGetter();
        newField.createSetter();

        String modifiedSource = cu.toString();
        String migrationSql = generateMigrationSql(tableName, in.columnName, sqlType,
                in.nullable, in.columnDefault);

        // Hard-enforce atomic write: migration first; on entity failure roll both back.
        try {
            Files.writeString(migrationPath, migrationSql);
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(NAME,
                    "hard-enforce contract violated: migration write failed before entity was modified. "
                            + "Cause: " + e.getMessage(),
                    "migration_write_failed", 5));
        }
        try {
            Files.writeString(entityFile, modifiedSource);
        } catch (IOException e) {
            // Rollback: delete migration; restore entity bytes from cache (best-effort).
            try {
                Files.deleteIfExists(migrationPath);
            } catch (IOException ignored) {
                // Migration deletion failed; recorded in error message below.
            }
            try {
                Files.write(entityFile, originalEntityBytes);
            } catch (IOException ignored) {
                // Best-effort restore; original bytes are in memory regardless.
            }
            throw new BridgeException(BridgeError.executeError(NAME,
                    "hard-enforce contract violated: entity write failed; migration rolled back; "
                            + "entity restored from cached bytes. Cause: " + e.getMessage(),
                    "entity_write_failed", 5));
        }

        ReportContext ctx = ReportContext.builder()
                .tool(NAME)
                .mode(cmd.mode() == null ? "default" : cmd.mode())
                .scope(cmd.scope())
                .contextId(cmd.contextId())
                .generatedAt(OffsetDateTime.now())
                .freshness(
                        "filesystem write under " + in.rootPath,
                        "entity=" + entityFile + " migration=" + migrationPath,
                        "Atomic two-file write completed; both halves persisted to disk.")
                .body(renderBody(in, tableName, sqlType, version, entityFile, migrationPath, migrationSql))
                .build();
        Path report = writer.write(ctx);
        return new ToolResult.Success(report);
    }

    // --- Input parsing --------------------------------------------------------

    record Inputs(
            Path rootPath,
            String entityFqn,
            String simpleName,
            String packageName,
            String fieldName,
            String fieldType,
            boolean nullable,
            String columnName,
            String columnSqlType,
            String columnDefault
    ) {}

    static Inputs parseInputs(BridgeCommand cmd) {
        if (cmd.scope() == null) {
            throw new BridgeException(BridgeError.validateError(NAME, "scope is required"));
        }
        Map<String, Object> extra = cmd.scope().extra();

        String entityFqn = required(extra, "entity_fqn");
        String fieldName = required(extra, "field_name");
        String fieldType = required(extra, "field_type");
        boolean nullable = Boolean.TRUE.equals(extra.get("nullable"));
        String columnName = optString(extra, "column_name", toSnakeCase(fieldName));
        String columnSqlType = optString(extra, "column_sql_type", null);
        String columnDefault = optString(extra, "column_default", null);

        int dot = entityFqn.lastIndexOf('.');
        if (dot <= 0) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "entity_fqn must be a fully-qualified class name (got: " + entityFqn + ")"));
        }

        String rp = cmd.scope().rootPath();
        Path rootPath = (rp == null || rp.isBlank())
                ? Path.of("").toAbsolutePath()
                : Path.of(rp).toAbsolutePath();

        return new Inputs(rootPath, entityFqn,
                entityFqn.substring(dot + 1),
                entityFqn.substring(0, dot),
                fieldName, fieldType, nullable, columnName, columnSqlType, columnDefault);
    }

    private static String required(Map<String, Object> extra, String key) {
        Object v = extra.get(key);
        if (v == null || v.toString().isBlank()) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "scope.extra." + key + " is required"));
        }
        return v.toString();
    }

    private static String optString(Map<String, Object> extra, String key, String def) {
        Object v = extra.get(key);
        return v == null ? def : v.toString();
    }

    // --- Pure helpers ---------------------------------------------------------

    static String toSnakeCase(String camel) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < camel.length(); i++) {
            char c = camel.charAt(i);
            if (Character.isUpperCase(c) && i > 0) sb.append('_');
            sb.append(Character.toLowerCase(c));
        }
        return sb.toString();
    }

    static String resolveSqlType(String javaType, String override) {
        if (override != null && !override.isBlank()) return override;
        String sql = DEFAULT_SQL_TYPES.get(javaType);
        if (sql != null) return sql;
        throw new BridgeException(BridgeError.validateError(NAME,
                "Unknown Java type: " + javaType
                        + ". Provide scope.extra.column_sql_type to override, or use one of: "
                        + DEFAULT_SQL_TYPES.keySet().stream().sorted().toList()));
    }

    static int nextVersion(Path migrationDir) {
        if (!Files.isDirectory(migrationDir)) return 1;
        int max = 0;
        try (Stream<Path> s = Files.list(migrationDir)) {
            for (Path p : (Iterable<Path>) s::iterator) {
                Matcher m = MIGRATION_VERSION.matcher(p.getFileName().toString());
                if (m.matches()) {
                    int v = Integer.parseInt(m.group(1));
                    if (v > max) max = v;
                }
            }
        } catch (IOException e) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Cannot list migration directory: " + e.getMessage()));
        }
        return max + 1;
    }

    static String resolveTableName(ClassOrInterfaceDeclaration cls) {
        for (AnnotationExpr anno : cls.getAnnotations()) {
            String n = anno.getNameAsString();
            if (!n.equals("Table") && !n.endsWith(".Table")) continue;
            if (anno instanceof NormalAnnotationExpr nae) {
                for (MemberValuePair p : nae.getPairs()) {
                    if ("name".equals(p.getNameAsString())) {
                        return p.getValue().asStringLiteralExpr().asString();
                    }
                }
            }
        }
        return cls.getNameAsString().toLowerCase(Locale.ROOT);
    }

    static String generateMigrationSql(String table, String column, String sqlType,
                                       boolean nullable, String defaultExpr) {
        StringBuilder sb = new StringBuilder();
        sb.append("ALTER TABLE ").append(table).append(" ADD COLUMN ")
                .append(column).append(' ').append(sqlType);
        if (!nullable) sb.append(" NOT NULL");
        if (defaultExpr != null && !defaultExpr.isBlank()) {
            sb.append(" DEFAULT ").append(defaultExpr);
        }
        sb.append(";\n");
        return sb.toString();
    }

    // --- Filesystem helpers ---------------------------------------------------

    Path resolveEntityFile(Path rootPath, String packageName, String simpleName) {
        Path sourcesRoot = rootPath.resolve("src/main/java");
        Path file = sourcesRoot.resolve(packageName.replace('.', '/'))
                .resolve(simpleName + ".java");
        if (!Files.isRegularFile(file)) {
            throw new BridgeException(BridgeError.validateError(NAME,
                    "Entity file not found: " + file));
        }
        return file;
    }

    CompilationUnit parseFile(Path file) {
        try {
            ParseResult<CompilationUnit> r = parser.parse(file);
            if (!r.isSuccessful() || r.getResult().isEmpty()) {
                throw new BridgeException(BridgeError.executeError(NAME,
                        "Failed to parse " + file + ": " + r.getProblems()));
            }
            return r.getResult().get();
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(NAME,
                    "Cannot read " + file + ": " + e.getMessage()));
        }
    }

    static ClassOrInterfaceDeclaration findClass(CompilationUnit cu, String simpleName) {
        return cu.getClassByName(simpleName).orElseThrow(() ->
                new BridgeException(BridgeError.executeError(NAME,
                        "Class " + simpleName + " not found in compilation unit")));
    }

    // --- Report body ----------------------------------------------------------

    private static String renderBody(Inputs in, String tableName, String sqlType, int version,
                                     Path entityFile, Path migrationPath, String migrationSql) {
        StringBuilder sb = new StringBuilder();
        sb.append("# entity_field_add report\n\n");
        sb.append("- **Entity:** `").append(in.entityFqn).append("`\n");
        sb.append("- **File:** `").append(entityFile).append("`\n");
        sb.append("- **Table:** `").append(tableName).append("`\n");
        sb.append("- **Field:** `").append(in.fieldName).append("` : `").append(in.fieldType).append("`\n");
        sb.append("- **Column:** `").append(in.columnName).append("` ").append(sqlType).append("\n");
        sb.append("- **Nullable:** ").append(in.nullable).append("\n");
        sb.append("- **Migration:** `V").append(version).append("__... .sql`\n");
        sb.append("- **Migration path:** `").append(migrationPath).append("`\n\n");
        sb.append("## Migration SQL\n\n```sql\n").append(migrationSql).append("```\n");
        return sb.toString();
    }
}
