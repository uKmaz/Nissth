package com.nissth.bridge.core;

import org.yaml.snakeyaml.DumperOptions;
import org.yaml.snakeyaml.Yaml;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.format.DateTimeFormatter;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Writes a Bridge report Markdown file to AgentReports/Bridge/. Step 5 of Phase 05.
 * Frontmatter conforms to bridge-command.schema.json $defs.reportFrontmatter.
 */
public class ReportWriter {

    static final DateTimeFormatter FILE_TS = DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HHmmss'Z'");

    private final Path bridgeReportsDir;

    /**
     * @param repoRoot absolute path to the Nissth repo root (the directory containing AgentReports/).
     */
    public ReportWriter(Path repoRoot) {
        this.bridgeReportsDir = repoRoot.resolve("AgentReports").resolve("Bridge");
    }

    /** For tests. */
    Path bridgeReportsDir() {
        return bridgeReportsDir;
    }

    /**
     * Write the report and return the absolute path. Throws BridgeException(format)
     * on I/O failure.
     */
    public Path write(ReportContext ctx) {
        try {
            Files.createDirectories(bridgeReportsDir);
            String fileName = chooseFileName(ctx);
            Path target = bridgeReportsDir.resolve(fileName);
            String content = buildContent(ctx);
            Files.writeString(target, content, StandardCharsets.UTF_8);
            return target;
        } catch (IOException e) {
            throw new BridgeException(BridgeError.formatError(ctx.tool(),
                    "Failed to write report: " + e.getMessage()));
        }
    }

    String chooseFileName(ReportContext ctx) {
        return ctx.tool() + "_" + ctx.generatedAt().format(FILE_TS) + ".md";
    }

    String buildContent(ReportContext ctx) {
        Map<String, Object> frontmatter = new LinkedHashMap<>();
        frontmatter.put("tool", ctx.tool());
        if (ctx.mode() != null) frontmatter.put("mode", ctx.mode());
        frontmatter.put("binding", ctx.binding());
        frontmatter.put("binding_version", ctx.bindingVersion());
        frontmatter.put("generated_at", ctx.generatedAt().toString());
        if (ctx.contextId() != null) frontmatter.put("context_id", ctx.contextId());
        if (ctx.scope() != null) frontmatter.put("scope", scopeToMap(ctx.scope()));

        Map<String, Object> freshness = new LinkedHashMap<>();
        freshness.put("source", ctx.freshness().source());
        freshness.put("source_state", ctx.freshness().sourceState());
        freshness.put("guarantee", ctx.freshness().guarantee());
        frontmatter.put("freshness", freshness);
        frontmatter.put("contract_version", ctx.contractVersion());

        DumperOptions opts = new DumperOptions();
        opts.setDefaultFlowStyle(DumperOptions.FlowStyle.BLOCK);
        opts.setPrettyFlow(true);
        Yaml yaml = new Yaml(opts);
        String yamlText = yaml.dump(frontmatter);

        StringBuilder sb = new StringBuilder();
        sb.append("---\n").append(yamlText).append("---\n\n");
        if (ctx.body() != null) sb.append(ctx.body());
        return sb.toString();
    }

    private Map<String, Object> scopeToMap(BridgeCommand.Scope s) {
        Map<String, Object> m = new LinkedHashMap<>();
        if (s.packageName() != null) m.put("package", s.packageName());
        if (s.rootPath() != null) m.put("root_path", s.rootPath());
        if (s.names() != null && !s.names().isEmpty()) m.put("names", s.names());
        if (s.fileExtension() != null) m.put("file_extension", s.fileExtension());
        if (s.tagFilter() != null) m.put("tag_filter", s.tagFilter());
        if (s.typeFilter() != null) m.put("type_filter", s.typeFilter());
        if (s.maxDepth() != null) m.put("max_depth", s.maxDepth());
        if (s.profile() != null) m.put("profile", s.profile());
        if (s.extra() != null && !s.extra().isEmpty()) m.put("extra", s.extra());
        return m;
    }
}
