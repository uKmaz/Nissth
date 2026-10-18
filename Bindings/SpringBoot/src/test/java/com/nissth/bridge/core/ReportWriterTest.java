package com.nissth.bridge.core;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.yaml.snakeyaml.Yaml;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

class ReportWriterTest {

    @Test
    void writes_report_with_valid_frontmatter(@TempDir Path repoRoot) throws IOException {
        ReportWriter writer = new ReportWriter(repoRoot);

        BridgeCommand.Scope scope = new BridgeCommand.Scope(
                "com.example", null, List.of(), null, null, null, 3, null, Map.of("extra_key", "extra_val"));

        ReportContext ctx = ReportContext.builder()
                .tool("compile_verify")
                .mode("default")
                .generatedAt(OffsetDateTime.of(2026, 5, 15, 14, 30, 0, 0, ZoneOffset.UTC))
                .scope(scope)
                .freshness("maven subprocess", "clean compile @ 2026-05-15T14:29:11Z",
                        "Clean rebuild; classpath fresh")
                .body("Status: CLEAN\n\nNo errors.\n")
                .build();

        Path written = writer.write(ctx);

        assertThat(written).exists();
        assertThat(written.getParent()).isEqualTo(repoRoot.resolve("AgentReports").resolve("Bridge"));
        assertThat(written.getFileName().toString())
                .startsWith("compile_verify_2026-05-15T143000Z");

        String content = Files.readString(written);
        assertThat(content).startsWith("---\n");
        assertThat(content).contains("Status: CLEAN");

        // Frontmatter must parse cleanly as YAML
        String yamlText = extractFrontmatter(content);
        Object parsed = new Yaml().load(yamlText);
        assertThat(parsed).isInstanceOf(Map.class);
        @SuppressWarnings("unchecked")
        Map<String, Object> fm = (Map<String, Object>) parsed;

        assertThat(fm)
                .containsEntry("tool", "compile_verify")
                .containsEntry("mode", "default")
                .containsEntry("binding", "spring-boot")
                .containsEntry("binding_version", "0.1.0")
                .containsEntry("contract_version", 1)
                .containsKey("generated_at")
                .containsKey("freshness")
                .containsKey("scope");

        @SuppressWarnings("unchecked")
        Map<String, Object> freshness = (Map<String, Object>) fm.get("freshness");
        assertThat(freshness)
                .containsEntry("source", "maven subprocess")
                .containsEntry("source_state", "clean compile @ 2026-05-15T14:29:11Z")
                .containsEntry("guarantee", "Clean rebuild; classpath fresh");
    }

    @Test
    void creates_AgentReports_Bridge_directory_if_missing(@TempDir Path repoRoot) {
        ReportWriter writer = new ReportWriter(repoRoot);
        assertThat(Files.exists(repoRoot.resolve("AgentReports").resolve("Bridge"))).isFalse();

        writer.write(minimalCtx());

        assertThat(Files.exists(repoRoot.resolve("AgentReports").resolve("Bridge"))).isTrue();
    }

    private ReportContext minimalCtx() {
        return ReportContext.builder()
                .tool("endpoint_lens")
                .freshness("ast", "files mtime 2026", "AST snapshot")
                .body("body")
                .build();
    }

    private String extractFrontmatter(String content) {
        int first = content.indexOf("---\n");
        int second = content.indexOf("---\n", first + 4);
        return content.substring(first + 4, second);
    }
}
