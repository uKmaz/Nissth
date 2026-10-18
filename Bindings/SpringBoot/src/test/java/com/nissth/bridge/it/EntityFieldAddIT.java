package com.nissth.bridge.it;

import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.EntityFieldAdd;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Stream;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Integration test for {@code entity_field_add} per Phase_05 §3 Step 17.
 *
 * <p>Copies the fixture's {@code Item} entity into a temp project root (so the
 * on-disk fixture is untouched), runs {@code entity_field_add} for a new
 * nullable {@code sku} column, and asserts BOTH artifacts of the atomic write
 * land: the modified entity contains the new field, and a new Flyway migration
 * exists with the expected SQL.
 */
class EntityFieldAddIT {

    @Test
    void success_path_writes_entity_and_flyway_migration(@TempDir Path projectRoot) throws IOException {
        Path fixture = ItSupport.fixtureRoot();

        Path entityDir = projectRoot.resolve("src/main/java/com/example/fixture");
        Files.createDirectories(entityDir);
        Path entityCopy = entityDir.resolve("Item.java");
        Files.writeString(entityCopy,
                Files.readString(fixture.resolve("src/main/java/com/example/fixture/Item.java")));

        Path migrationDir = projectRoot.resolve("src/main/resources/db/migration");
        Files.createDirectories(migrationDir);
        Files.writeString(migrationDir.resolve("V1__init.sql"),
                Files.readString(fixture.resolve("src/main/resources/db/migration/V1__init.sql")));

        ReportWriter writer = new ReportWriter(projectRoot);
        EntityFieldAdd tool = new EntityFieldAdd(writer);

        Map<String, Object> extra = new LinkedHashMap<>();
        extra.put("entity_fqn", "com.example.fixture.Item");
        extra.put("field_name", "sku");
        extra.put("field_type", "String");
        extra.put("nullable", true);

        BridgeCommand cmd = new BridgeCommand(
                "entity_field_add", null, null,
                new BridgeCommand.Scope(null, projectRoot.toString(), List.of(),
                        null, null, null, null, null, extra),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);

        String entityNow = Files.readString(entityCopy);
        assertThat(entityNow)
                .as("entity must now declare the sku column")
                .contains("private String sku;")
                .contains("@Column(name = \"sku\"");

        Path expectedMigration;
        try (Stream<Path> walk = Files.list(migrationDir)) {
            expectedMigration = walk
                    .filter(p -> p.getFileName().toString().endsWith("__add_sku_to_items.sql"))
                    .findFirst()
                    .orElseThrow(() -> new AssertionError(
                            "Expected V<n>__add_sku_to_items.sql in " + migrationDir));
        }
        String migrationSql = Files.readString(expectedMigration);
        assertThat(migrationSql)
                .as("migration SQL must add the column with the mapped PG type")
                .contains("ALTER TABLE items ADD COLUMN sku VARCHAR(255)")
                .doesNotContain("NOT NULL");

        Path report = ((ToolResult.Success) result).reportPath();
        Set<ValidationMessage> errors = ItSupport.validateFrontmatter(report);
        assertThat(errors).as("entity_field_add report frontmatter schema errors").isEmpty();
    }
}
