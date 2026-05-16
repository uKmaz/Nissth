package com.nissth.bridge.tools;

import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

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
}
