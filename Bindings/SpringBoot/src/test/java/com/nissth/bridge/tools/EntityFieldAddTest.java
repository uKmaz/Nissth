package com.nissth.bridge.tools;

import com.github.javaparser.StaticJavaParser;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.ToolResult;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class EntityFieldAddTest {

    private static final String ITEM_ENTITY_SOURCE = """
            package com.example.fixture;

            import jakarta.persistence.Entity;
            import jakarta.persistence.Id;
            import jakarta.persistence.Table;

            @Entity
            @Table(name = "items")
            public class Item {
                @Id
                private Long id;
                private String name;

                public Long getId() { return id; }
                public void setId(Long id) { this.id = id; }
                public String getName() { return name; }
                public void setName(String name) { this.name = name; }
            }
            """;

    private static BridgeCommand command(Path target, Map<String, Object> extra) {
        return new BridgeCommand("entity_field_add", null, null,
                new BridgeCommand.Scope(null, target.toString(), List.of(),
                        null, null, null, null, null, extra),
                BridgeCommand.Output.defaults());
    }

    private static Path setupItemEntity(Path root) throws IOException {
        Path pkg = root.resolve("src/main/java/com/example/fixture");
        Files.createDirectories(pkg);
        Path entity = pkg.resolve("Item.java");
        Files.writeString(entity, ITEM_ENTITY_SOURCE);
        Files.createDirectories(root.resolve("src/main/resources/db/migration"));
        return entity;
    }

    private static String sha256(Path p) throws IOException {
        try {
            return HexFormat.of().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(p)));
        } catch (NoSuchAlgorithmException e) {
            throw new AssertionError(e);
        }
    }

    // --- Pure helpers --------------------------------------------------------

    @Test
    void resolveSqlType_maps_String_to_VARCHAR_255() {
        assertThat(EntityFieldAdd.resolveSqlType("String", null)).isEqualTo("VARCHAR(255)");
        assertThat(EntityFieldAdd.resolveSqlType("java.lang.String", null)).isEqualTo("VARCHAR(255)");
    }

    @Test
    void resolveSqlType_maps_temporal_types() {
        assertThat(EntityFieldAdd.resolveSqlType("LocalDate", null)).isEqualTo("DATE");
        assertThat(EntityFieldAdd.resolveSqlType("java.time.OffsetDateTime", null)).isEqualTo("TIMESTAMPTZ");
        assertThat(EntityFieldAdd.resolveSqlType("java.time.Instant", null)).isEqualTo("TIMESTAMPTZ");
    }

    @Test
    void resolveSqlType_override_takes_precedence() {
        assertThat(EntityFieldAdd.resolveSqlType("java.util.Map", "JSONB")).isEqualTo("JSONB");
        assertThat(EntityFieldAdd.resolveSqlType("String", "TEXT")).isEqualTo("TEXT");
    }

    @Test
    void resolveSqlType_unknown_type_without_override_throws_validate() {
        assertThatThrownBy(() -> EntityFieldAdd.resolveSqlType("com.example.WeirdType", null))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.stage().name()).isEqualTo("VALIDATE");
                    assertThat(err.error())
                            .contains("Unknown Java type")
                            .contains("column_sql_type");
                });
    }

    @Test
    void nextVersion_empty_directory_returns_1(@TempDir Path dir) {
        assertThat(EntityFieldAdd.nextVersion(dir)).isEqualTo(1);
    }

    @Test
    void nextVersion_picks_max_plus_one(@TempDir Path dir) throws IOException {
        Files.writeString(dir.resolve("V1__init.sql"), "");
        Files.writeString(dir.resolve("V2__qty.sql"), "");
        Files.writeString(dir.resolve("V5__skip.sql"), "");
        Files.writeString(dir.resolve("README.md"), "ignored");
        Files.writeString(dir.resolve("not_versioned.sql"), "ignored");
        assertThat(EntityFieldAdd.nextVersion(dir)).isEqualTo(6);
    }

    @Test
    void resolveTableName_uses_Table_annotation_name() {
        CompilationUnit cu = StaticJavaParser.parse(ITEM_ENTITY_SOURCE);
        ClassOrInterfaceDeclaration cls = cu.getClassByName("Item").orElseThrow();
        assertThat(EntityFieldAdd.resolveTableName(cls)).isEqualTo("items");
    }

    @Test
    void resolveTableName_falls_back_to_lowercase_class_name() {
        String src = "package x; import jakarta.persistence.Entity; @Entity public class Widget {}";
        CompilationUnit cu = StaticJavaParser.parse(src);
        ClassOrInterfaceDeclaration cls = cu.getClassByName("Widget").orElseThrow();
        assertThat(EntityFieldAdd.resolveTableName(cls)).isEqualTo("widget");
    }

    @Test
    void generateMigrationSql_nullable_omits_NOT_NULL_and_DEFAULT() {
        String sql = EntityFieldAdd.generateMigrationSql("items", "tag", "VARCHAR(64)", true, null);
        assertThat(sql).isEqualTo("ALTER TABLE items ADD COLUMN tag VARCHAR(64);\n");
    }

    @Test
    void generateMigrationSql_not_null_with_default() {
        String sql = EntityFieldAdd.generateMigrationSql("items", "qty", "INTEGER", false, "0");
        assertThat(sql).isEqualTo("ALTER TABLE items ADD COLUMN qty INTEGER NOT NULL DEFAULT 0;\n");
    }

    @Test
    void toSnakeCase_converts_camelCase() {
        assertThat(EntityFieldAdd.toSnakeCase("qtyInStock")).isEqualTo("qty_in_stock");
        assertThat(EntityFieldAdd.toSnakeCase("name")).isEqualTo("name");
        assertThat(EntityFieldAdd.toSnakeCase("OrderId")).isEqualTo("order_id");
    }

    // --- End-to-end ---------------------------------------------------------

    @Test
    void success_path_writes_entity_and_migration(@TempDir Path root) throws IOException {
        Path entity = setupItemEntity(root);
        ReportWriter writer = new ReportWriter(root);
        EntityFieldAdd tool = new EntityFieldAdd(writer);

        ToolResult result = tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qtyInStock",
                "field_type", "Integer",
                "nullable", false,
                "column_default", "0")));

        assertThat(result).isInstanceOf(ToolResult.Success.class);
        String updated = Files.readString(entity);
        assertThat(updated)
                .contains("@Column(name = \"qty_in_stock\", nullable = false)")
                .contains("private Integer qtyInStock;")
                .contains("getQtyInStock()")
                .contains("setQtyInStock(Integer");

        Path migration = root.resolve("src/main/resources/db/migration/V1__add_qty_in_stock_to_items.sql");
        assertThat(migration).exists();
        assertThat(Files.readString(migration))
                .isEqualTo("ALTER TABLE items ADD COLUMN qty_in_stock INTEGER NOT NULL DEFAULT 0;\n");
    }

    @Test
    void success_path_with_FQN_field_type_adds_import(@TempDir Path root) throws IOException {
        Path entity = setupItemEntity(root);
        ReportWriter writer = new ReportWriter(root);
        EntityFieldAdd tool = new EntityFieldAdd(writer);

        ToolResult result = tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "createdAt",
                "field_type", "java.time.OffsetDateTime",
                "nullable", true)));

        assertThat(result).isInstanceOf(ToolResult.Success.class);
        String updated = Files.readString(entity);
        assertThat(updated)
                .contains("import java.time.OffsetDateTime;")
                .contains("private OffsetDateTime createdAt;");

        Path migration = root.resolve("src/main/resources/db/migration/V1__add_created_at_to_items.sql");
        assertThat(Files.readString(migration))
                .isEqualTo("ALTER TABLE items ADD COLUMN created_at TIMESTAMPTZ;\n");
    }

    @Test
    void unknown_field_type_short_circuits_with_validate(@TempDir Path root) throws IOException {
        setupItemEntity(root);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "weird",
                "field_type", "com.example.WeirdType",
                "nullable", true))))
                .isInstanceOf(BridgeException.class);
        assertThat(root.resolve("src/main/resources/db/migration"))
                .isEmptyDirectory();
    }

    @Test
    void non_nullable_without_column_default_throws_validate(@TempDir Path root) throws IOException {
        setupItemEntity(root);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", false))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.stage().name()).isEqualTo("VALIDATE");
                    assertThat(err.error()).contains("column_default");
                });
    }

    @Test
    void nextVersion_bumps_past_existing_migration_with_same_slug(@TempDir Path root) throws IOException {
        // The Files.exists collision-check in run() is defensive against external interference
        // (e.g., a concurrent process writing the same filename between nextVersion() and
        // Files.writeString). nextVersion() itself ALWAYS picks max+1 across the dir, so a
        // pre-existing file with the same slug just bumps the version — not a collision.
        // The genuine concurrent-process collision path is exercised by Step 18 contract tests.
        Path entity = setupItemEntity(root);
        Path priorMigration = root.resolve("src/main/resources/db/migration/V1__add_qty_to_items.sql");
        Files.writeString(priorMigration, "-- prior migration\n");
        String entityShaBefore = sha256(entity);

        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        ToolResult result = tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", true)));

        assertThat(result).isInstanceOf(ToolResult.Success.class);
        // Prior V1 stays exactly as written; tool wrote V2 alongside it.
        assertThat(Files.readString(priorMigration)).isEqualTo("-- prior migration\n");
        assertThat(root.resolve("src/main/resources/db/migration/V2__add_qty_to_items.sql")).exists();
        // Entity SHA changed (field was added), as expected for the success path.
        assertThat(sha256(entity)).isNotEqualTo(entityShaBefore);
    }

    @Test
    void missing_entity_file_throws_validate(@TempDir Path root) throws IOException {
        Files.createDirectories(root.resolve("src/main/resources/db/migration"));
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.absent.Ghost",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", true))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> assertThat(((BridgeException) e).error().error())
                        .contains("Entity file not found"));
    }

    @Test
    void missing_migration_directory_throws_validate(@TempDir Path root) throws IOException {
        Path pkg = root.resolve("src/main/java/com/example/fixture");
        Files.createDirectories(pkg);
        Files.writeString(pkg.resolve("Item.java"), ITEM_ENTITY_SOURCE);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", true))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> assertThat(((BridgeException) e).error().error())
                        .contains("Migration directory not found"));
    }

    @Test
    void duplicate_field_throws_validate(@TempDir Path root) throws IOException {
        Path entity = setupItemEntity(root);
        String entitySha = sha256(entity);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "name",     // already exists in fixture
                "field_type", "String",
                "nullable", true))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> assertThat(((BridgeException) e).error().error())
                        .contains("already has a field named"));
        assertThat(sha256(entity)).isEqualTo(entitySha);
    }

    @Test
    void missing_required_input_throws_validate(@TempDir Path root) throws IOException {
        setupItemEntity(root);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                // entity_fqn missing
                "field_name", "qty",
                "field_type", "Integer"))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> assertThat(((BridgeException) e).error().error())
                        .contains("entity_fqn"));
    }
}
