package com.nissth.bridge.tools;

import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;

class EntityLensTest {

    private static final String ITEM_ENTITY_SRC = """
            package com.example.domain;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "items")
            public class Item {

                @Id
                @GeneratedValue(strategy = GenerationType.IDENTITY)
                private Long id;

                @Column(name = "display_name", nullable = false)
                private String displayName;

                @Column(nullable = true)
                private Integer qty;

                @ManyToOne
                private Category category;

                @OneToMany(mappedBy = "item")
                private List<Tag> tags;
            }
            """;

    @Test
    void extracts_entity_with_table_name(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(ITEM_ENTITY_SRC, null, "synthetic", out);

        assertThat(out).hasSize(1);
        EntityLens.Entity e = out.get(0);
        assertThat(e.fqn()).isEqualTo("com.example.domain.Item");
        assertThat(e.tableName()).isEqualTo("items");
    }

    @Test
    void extracts_columns_with_pk_and_nullable(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(ITEM_ENTITY_SRC, null, "synthetic", out);

        List<EntityLens.Column> cols = out.get(0).columns();
        assertThat(cols).hasSize(3);

        EntityLens.Column id = cols.stream().filter(c -> c.primaryKey()).findFirst().orElseThrow();
        assertThat(id.javaType()).isEqualTo("Long");

        EntityLens.Column displayName = cols.stream()
                .filter(c -> c.columnName().equals("display_name")).findFirst().orElseThrow();
        assertThat(displayName.nullable()).isFalse();
        assertThat(displayName.primaryKey()).isFalse();

        EntityLens.Column qty = cols.stream().filter(c -> c.columnName().equals("qty")).findFirst().orElseThrow();
        assertThat(qty.nullable()).isTrue();
    }

    @Test
    void extracts_relationships_with_ownership(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(ITEM_ENTITY_SRC, null, "synthetic", out);

        List<EntityLens.Relationship> rels = out.get(0).relationships();
        assertThat(rels).hasSize(2);

        EntityLens.Relationship many = rels.stream()
                .filter(r -> r.kind().equals("ManyToOne")).findFirst().orElseThrow();
        assertThat(many.field()).isEqualTo("category");
        assertThat(many.owning()).isTrue();

        EntityLens.Relationship one = rels.stream()
                .filter(r -> r.kind().equals("OneToMany")).findFirst().orElseThrow();
        assertThat(one.owning()).isFalse();
        assertThat(one.mappedBy()).isEqualTo("item");
    }

    @Test
    void table_name_defaults_to_lowercase_class_name_when_no_Table_annotation(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        String src = """
                package com.example.domain;
                import jakarta.persistence.*;

                @Entity
                public class Order {
                    @Id private Long id;
                }
                """;
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(src, null, "synthetic", out);

        assertThat(out).hasSize(1);
        assertThat(out.get(0).tableName()).isEqualTo("order");
    }

    @Test
    void column_name_defaults_to_snake_case_of_field(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        String src = """
                package com.example.domain;
                import jakarta.persistence.*;

                @Entity
                public class Product {
                    @Id private Long id;
                    private String firstName;
                    private String lastNameUpper;
                }
                """;
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(src, null, "synthetic", out);

        List<String> cols = out.get(0).columns().stream().map(EntityLens.Column::columnName).toList();
        assertThat(cols).contains("first_name", "last_name_upper");
    }

    @Test
    void non_entity_classes_are_ignored(@TempDir Path repoRoot) {
        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        String src = """
                package com.example;
                public class Pojo {
                    private Long id;
                }
                """;
        List<EntityLens.Entity> out = new ArrayList<>();

        tool.extractFromSource(src, null, "synthetic", out);

        assertThat(out).isEmpty();
    }

    @Test
    void snake_case_helper() {
        assertThat(EntityLens.toSnakeCase("expirationDate")).isEqualTo("expiration_date");
        assertThat(EntityLens.toSnakeCase("id")).isEqualTo("id");
        assertThat(EntityLens.toSnakeCase("URL")).isEqualTo("u_r_l");
    }

    @Test
    void extractClaimedSchema_parses_columns_under_each_table_heading(@TempDir Path dir) throws IOException {
        Path artifact = dir.resolve("items.md");
        Files.writeString(artifact, """
                ---
                artifact_type: schema_index
                ---

                ## Table: items

                | Column      | Type         | Nullable | Notes |
                |:------------|:-------------|:---------|:------|
                | id          | BIGINT       | NO       | PK    |
                | name        | VARCHAR(255) | NO       |       |
                | description | TEXT         | YES      |       |

                ## Table: tags

                | Column | Type    | Nullable |
                |:-------|:--------|:---------|
                | id     | BIGINT  | NO       |
                | label  | VARCHAR | NO       |
                """);

        Map<String, Set<String>> schema = EntityLens.extractClaimedSchema(artifact);
        assertThat(schema).containsOnlyKeys("items", "tags");
        assertThat(schema.get("items")).containsExactlyInAnyOrder("id", "name", "description");
        assertThat(schema.get("tags")).containsExactlyInAnyOrder("id", "label");
    }

    @Test
    void extractClaimedSchema_returns_empty_for_artifact_without_table_headings(@TempDir Path dir) throws IOException {
        Path artifact = dir.resolve("notes.md");
        Files.writeString(artifact, """
                ---
                artifact_type: schema_index
                ---

                Just prose, no `## Table:` headings here.
                """);
        assertThat(EntityLens.extractClaimedSchema(artifact)).isEmpty();
    }

    @Test
    void drift_fires_when_claimed_column_set_differs_from_live(@TempDir Path repoRoot) throws IOException {
        Path dblDir = repoRoot.resolve("DBL").resolve("SchemaIndex");
        Files.createDirectories(dblDir);
        Path artifact = dblDir.resolve("items.md");
        Path scanRoot = repoRoot.resolve("src").resolve("main").resolve("java");
        Files.createDirectories(scanRoot);
        Files.writeString(artifact, """
                ---
                artifact_type: schema_index
                last_regenerated: 2026-01-01 by hand
                covers:
                  - %s
                ---

                ## Table: items

                | Column      | Type         | Nullable |
                |:------------|:-------------|:---------|
                | id          | BIGINT       | NO       |
                | name        | VARCHAR(255) | NO       |
                | description | TEXT         | YES      |
                """.formatted(scanRoot.toString().replace("\\", "/")));

        Path entityFile = scanRoot.resolve("Item.java");
        Files.writeString(entityFile, """
                package com.example;

                import jakarta.persistence.*;

                @Entity
                @Table(name = "items")
                public class Item {
                    @Id private Long id;
                    private String name;
                    private Integer qty;
                }
                """);

        EntityLens tool = new EntityLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        com.nissth.bridge.core.BridgeCommand cmd = new com.nissth.bridge.core.BridgeCommand(
                "entity_lens", null, null,
                new com.nissth.bridge.core.BridgeCommand.Scope(null, scanRoot.toString(), List.of(),
                        null, null, null, null, null, java.util.Map.of()),
                com.nissth.bridge.core.BridgeCommand.Output.defaults());

        tool.run(cmd);

        String after = Files.readString(artifact);
        assertThat(after)
                .as("column drift (live=qty, claimed=description) must trigger STALE-flip")
                .contains("STALE")
                .contains("superseded by AgentReports/Bridge/");
    }
}
