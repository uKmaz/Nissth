package com.nissth.bridge.it;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.networknt.schema.JsonSchema;
import com.networknt.schema.JsonSchemaFactory;
import com.networknt.schema.SchemaValidatorsConfig;
import com.networknt.schema.SpecVersion;
import com.networknt.schema.ValidationMessage;
import org.yaml.snakeyaml.Yaml;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Shared helpers for Phase_05 §3 Step 17 integration tests:
 * locates the on-disk fixture project, and validates produced Bridge report
 * frontmatter against {@code $defs.reportFrontmatter} in
 * {@code bridge-command.schema.json}.
 */
final class ItSupport {

    private static final ObjectMapper MAPPER = new ObjectMapper();
    private static volatile JsonSchema FRONTMATTER_SCHEMA;

    private ItSupport() {}

    /**
     * Resolve the Maven fixture project root. Tests run with cwd =
     * {@code Bindings/SpringBoot/}; the fixture lives at {@code tests/fixture/}.
     */
    static Path fixtureRoot() {
        Path candidate = Path.of("tests", "fixture").toAbsolutePath();
        assertThat(candidate)
                .as("fixture project root (cwd should be Bindings/SpringBoot)")
                .isDirectory();
        assertThat(candidate.resolve("pom.xml"))
                .as("fixture pom.xml")
                .isRegularFile();
        return candidate;
    }

    /**
     * Validate the YAML frontmatter of a Bridge report against the report-frontmatter sub-schema.
     * Asserts the opening {@code ---} is at offset 0 (a contract on ReportWriter's output shape).
     */
    static Set<ValidationMessage> validateFrontmatter(Path report) throws IOException {
        String full = Files.readString(report);
        int first = full.indexOf("---");
        int second = full.indexOf("---", first + 3);
        assertThat(first).as("opening frontmatter delimiter at offset 0").isZero();
        assertThat(second).as("closing frontmatter delimiter present").isGreaterThan(first);

        String yaml = full.substring(first + 3, second).trim();
        Object parsed = new Yaml().load(yaml);
        assertThat(parsed).as("YAML frontmatter parses to non-null").isNotNull();
        JsonNode asJson = MAPPER.valueToTree(parsed);

        return frontmatterSchema().validate(asJson);
    }

    private static JsonSchema frontmatterSchema() throws IOException {
        JsonSchema cached = FRONTMATTER_SCHEMA;
        if (cached != null) return cached;
        try (InputStream in = ItSupport.class.getResourceAsStream("/bridge-command.schema.json")) {
            assertThat(in).as("bridge-command.schema.json on classpath").isNotNull();
            JsonNode root = MAPPER.readTree(in);
            JsonNode sub = root.at("/$defs/reportFrontmatter");
            assertThat(sub.isMissingNode())
                    .as("/$defs/reportFrontmatter must exist in the schema")
                    .isFalse();
            JsonSchemaFactory factory = JsonSchemaFactory.getInstance(SpecVersion.VersionFlag.V202012);
            JsonSchema schema = factory.getSchema(sub, new SchemaValidatorsConfig());
            FRONTMATTER_SCHEMA = schema;
            return schema;
        }
    }
}
