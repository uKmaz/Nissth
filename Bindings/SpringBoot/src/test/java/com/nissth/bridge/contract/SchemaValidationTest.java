package com.nissth.bridge.contract;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.JsonSchemaFactory;
import com.networknt.schema.SchemaValidatorsConfig;
import com.networknt.schema.SpecVersion;
import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.EndpointLens;
import com.nissth.bridge.tools.EntityFieldAdd;
import com.nissth.bridge.tools.EntityLens;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.yaml.snakeyaml.Yaml;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Schema-validation harness per Phase_05 §3 Step 19. For each report-producing
 * tool invocation, parses the produced YAML frontmatter and validates it against
 * {@code Bindings/_schemas/bridge-command.schema.json $defs.reportFrontmatter}
 * (CLAUDE.md §11.3).
 *
 * <p>This is the runtime guarantee that ReportWriter's output keeps matching the
 * cross-stack contract — independent of which tool produced the report.
 */
class SchemaValidationTest {

    private static JsonSchema frontmatterSchema;
    private static final ObjectMapper MAPPER = new ObjectMapper();

    @BeforeAll
    static void loadSubSchema() throws IOException {
        try (InputStream in = SchemaValidationTest.class.getResourceAsStream("/bridge-command.schema.json")) {
            assertThat(in).as("bridge-command.schema.json on classpath").isNotNull();
            JsonNode root = MAPPER.readTree(in);
            JsonNode sub = root.at("/$defs/reportFrontmatter");
            assertThat(sub.isMissingNode())
                    .as("/$defs/reportFrontmatter must exist in the schema")
                    .isFalse();
            JsonSchemaFactory factory = JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012);
            SchemaValidatorsConfig cfg = new SchemaValidatorsConfig();
            frontmatterSchema = factory.getSchema(sub, cfg);
        }
    }

    private static Set<ValidationMessage> validateFrontmatter(Path report) throws IOException {
        String full = Files.readString(report);
        int first = full.indexOf("---");
        int second = full.indexOf("---", first + 3);
        assertThat(first).as("opening frontmatter delimiter").isZero();
        assertThat(second).as("closing frontmatter delimiter").isGreaterThan(first);
        String yaml = full.substring(first + 3, second).trim();

        Object parsed = new Yaml().load(yaml);
        assertThat(parsed).as("YAML parses to a non-null object").isNotNull();
        JsonNode asJson = MAPPER.valueToTree(parsed);
        return frontmatterSchema.validate(asJson);
    }

    // --- Fixtures -----------------------------------------------------------

    private static final String CONTROLLER_SOURCE = """
            package com.example.fixture;

            import org.springframework.web.bind.annotation.GetMapping;
            import org.springframework.web.bind.annotation.RequestMapping;
            import org.springframework.web.bind.annotation.RestController;

            @RestController
            @RequestMapping("/api/items")
            public class ItemController {
                @GetMapping
                public String list() { return "[]"; }
            }
            """;

    private static final String ENTITY_SOURCE = """
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

    private static Path writeJava(Path root, String simpleName, String source) throws IOException {
        Path pkg = root.resolve("src/main/java/com/example/fixture");
        Files.createDirectories(pkg);
        Path file = pkg.resolve(simpleName + ".java");
        Files.writeString(file, source);
        return file;
    }

    private static BridgeCommand cmd(String tool, Path scanRoot, Map<String, Object> extra) {
        return new BridgeCommand(tool, null, null,
                new BridgeCommand.Scope(null, scanRoot.toString(), List.of(),
                        null, null, null, null, null, extra == null ? Map.of() : extra),
                BridgeCommand.Output.defaults());
    }

    // --- Per-tool validation -----------------------------------------------

    @Test
    void endpoint_lens_report_frontmatter_conforms_to_schema(@TempDir Path root) throws IOException {
        writeJava(root, "ItemController", CONTROLLER_SOURCE);
        ReportWriter writer = new ReportWriter(root);
        EndpointLens tool = new EndpointLens(writer, new StaleFlipper(root));

        ToolResult result = tool.run(cmd("endpoint_lens", root, null));
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        Path report = ((ToolResult.Success) result).reportPath();

        Set<ValidationMessage> errors = validateFrontmatter(report);
        assertThat(errors).as("endpoint_lens report frontmatter validation errors").isEmpty();
    }

    @Test
    void entity_lens_report_frontmatter_conforms_to_schema(@TempDir Path root) throws IOException {
        writeJava(root, "Item", ENTITY_SOURCE);
        ReportWriter writer = new ReportWriter(root);
        EntityLens tool = new EntityLens(writer, new StaleFlipper(root));

        ToolResult result = tool.run(cmd("entity_lens", root, null));
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        Path report = ((ToolResult.Success) result).reportPath();

        Set<ValidationMessage> errors = validateFrontmatter(report);
        assertThat(errors).as("entity_lens report frontmatter validation errors").isEmpty();
    }

    @Test
    void entity_field_add_report_frontmatter_conforms_to_schema(@TempDir Path root) throws IOException {
        writeJava(root, "Item", ENTITY_SOURCE);
        Files.createDirectories(root.resolve("src/main/resources/db/migration"));

        ReportWriter writer = new ReportWriter(root);
        EntityFieldAdd tool = new EntityFieldAdd(writer);

        Map<String, Object> extra = new LinkedHashMap<>();
        extra.put("entity_fqn", "com.example.fixture.Item");
        extra.put("field_name", "qty");
        extra.put("field_type", "Integer");
        extra.put("nullable", false);
        extra.put("column_default", "0");

        ToolResult result = tool.run(cmd("entity_field_add", root, extra));
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        Path report = ((ToolResult.Success) result).reportPath();

        Set<ValidationMessage> errors = validateFrontmatter(report);
        assertThat(errors).as("entity_field_add report frontmatter validation errors").isEmpty();
    }

    @Test
    void frontmatter_validator_rejects_missing_required_field() {
        // Self-check: confirm the loaded sub-schema actually enforces required fields.
        Map<String, Object> bad = new LinkedHashMap<>();
        bad.put("tool", "compile_verify");
        bad.put("binding", "spring-boot");
        bad.put("binding_version", "0.1.0");
        // Deliberately omit `generated_at`, `freshness`, `contract_version`.
        Set<ValidationMessage> errors = frontmatterSchema.validate(MAPPER.valueToTree(bad));
        assertThat(errors)
                .as("schema should flag missing required fields")
                .isNotEmpty();
    }

    @Test
    void frontmatter_validator_rejects_wrong_contract_version() {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("tool", "compile_verify");
        body.put("binding", "spring-boot");
        body.put("binding_version", "0.1.0");
        body.put("generated_at", "2026-05-17T00:00:00+00:00");
        body.put("freshness", Map.of(
                "source", "stub",
                "source_state", "stub",
                "guarantee", "stub"));
        body.put("contract_version", 2);   // schema requires const 1

        Set<ValidationMessage> errors = frontmatterSchema.validate(MAPPER.valueToTree(body));
        assertThat(errors)
                .as("schema should flag contract_version != 1")
                .isNotEmpty();
    }
}
