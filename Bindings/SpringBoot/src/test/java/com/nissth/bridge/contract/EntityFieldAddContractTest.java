package com.nissth.bridge.contract;

import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.EntityFieldAdd;
import org.junit.jupiter.api.AfterEach;
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

/**
 * Hard-enforce contract tests for {@code entity_field_add} per Phase_05 §3 Step 18.
 *
 * <p>Unit tests in {@code tools.EntityFieldAddTest} cover validate-stage paths via
 * a real temp filesystem. This class covers the EXECUTE-STAGE rollback path that
 * the unit tests cannot trigger: a real filesystem fault between the migration
 * write and the entity write must roll BOTH halves back, exit 5, and leave the
 * entity SHA unchanged.
 */
class EntityFieldAddContractTest {

    private static final String ITEM_SOURCE = """
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
            }
            """;

    private Path entity;

    @AfterEach
    void restoreEntityWritability() {
        // @TempDir cleanup requires the file to be writable, otherwise the test
        // session leaks files. Restore writability if the test made it read-only.
        if (entity != null && Files.exists(entity)) {
            //noinspection ResultOfMethodCallIgnored
            entity.toFile().setWritable(true, false);
        }
    }

    private static BridgeCommand command(Path root, Map<String, Object> extra) {
        return new BridgeCommand("entity_field_add", null, null,
                new BridgeCommand.Scope(null, root.toString(), List.of(),
                        null, null, null, null, null, extra),
                BridgeCommand.Output.defaults());
    }

    private static String sha256(Path p) throws IOException {
        try {
            return HexFormat.of().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(p)));
        } catch (NoSuchAlgorithmException e) {
            throw new AssertionError(e);
        }
    }

    private Path setUpFixture(Path root) throws IOException {
        Path pkg = root.resolve("src/main/java/com/example/fixture");
        Files.createDirectories(pkg);
        Path file = pkg.resolve("Item.java");
        Files.writeString(file, ITEM_SOURCE);
        Files.createDirectories(root.resolve("src/main/resources/db/migration"));
        return file;
    }

    @Test
    void readonly_entity_file_triggers_full_rollback_with_exit_5_and_unchanged_SHA(@TempDir Path root) throws IOException {
        // Arrange: real fixture + read-only entity. Migration directory is writable
        // so the FIRST write (migration .sql) succeeds and the SECOND write (entity .java)
        // is the one that fails, exercising the rollback path.
        entity = setUpFixture(root);
        String shaBefore = sha256(entity);
        //noinspection ResultOfMethodCallIgnored
        entity.toFile().setReadOnly();

        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));
        Path expectedMigration = root.resolve(
                "src/main/resources/db/migration/V1__add_qty_to_items.sql");

        // Act / Assert: exit 5 with the correct error_code; both halves rolled back.
        assertThatThrownBy(() -> tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", true))))
                .isInstanceOf(BridgeException.class)
                .satisfies(e -> {
                    var err = ((BridgeException) e).error();
                    assertThat(err.exitCode()).isEqualTo(5);
                    assertThat(err.errorCode()).isEqualTo("entity_write_failed");
                    assertThat(err.stage().name()).isEqualTo("EXECUTE");
                    assertThat(err.error())
                            .contains("hard-enforce contract violated")
                            .contains("rolled back");
                });

        // Migration file must NOT exist — was rolled back.
        assertThat(expectedMigration).doesNotExist();
        // Entity SHA must match pre-test value — its content was never overwritten.
        assertThat(sha256(entity)).isEqualTo(shaBefore);
    }

    @Test
    void success_path_writes_both_artifacts_and_returns_Success(@TempDir Path root) throws IOException {
        entity = setUpFixture(root);
        EntityFieldAdd tool = new EntityFieldAdd(new ReportWriter(root));

        ToolResult result = tool.run(command(root, Map.of(
                "entity_fqn", "com.example.fixture.Item",
                "field_name", "qty",
                "field_type", "Integer",
                "nullable", false,
                "column_default", "0")));

        assertThat(result).isInstanceOf(ToolResult.Success.class);

        Path migration = root.resolve("src/main/resources/db/migration/V1__add_qty_to_items.sql");
        assertThat(migration).exists();
        assertThat(Files.readString(migration))
                .isEqualTo("ALTER TABLE items ADD COLUMN qty INTEGER NOT NULL DEFAULT 0;\n");

        String entityNow = Files.readString(entity);
        assertThat(entityNow)
                .contains("@Column(name = \"qty\", nullable = false)")
                .contains("private Integer qty;");
    }
}
