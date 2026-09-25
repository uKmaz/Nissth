package com.nissth.bridge.core;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.yaml.snakeyaml.DumperOptions;
import org.yaml.snakeyaml.Yaml;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/**
 * Detects drift between a freshly-generated Bridge report and existing DBL artifacts;
 * rewrites the affected DBL artifact's frontmatter with a STALE marker. Step 6 of Phase 05.
 *
 * <p>This is the runtime mechanism that converts CLAUDE.md §11.4 (stale-flip) from
 * agent discipline into mechanical enforcement.
 */
public class StaleFlipper {

    private static final Logger LOG = LoggerFactory.getLogger(StaleFlipper.class);
    private static final Pattern BOUNDARY = Pattern.compile("^---\\s*\\R", Pattern.MULTILINE);

    private final Path dblRoot;

    /** @param repoRoot absolute path to the project root (containing DBL/). */
    public StaleFlipper(Path repoRoot) {
        this.dblRoot = repoRoot.resolve("DBL");
    }

    /** For tests. */
    Path dblRoot() {
        return dblRoot;
    }

    /**
     * Callback: given a DBL artifact's path and parsed frontmatter, decide whether
     * the artifact has drifted from the live state captured in the Bridge report.
     * Returning true triggers a STALE-flip on that artifact.
     */
    @FunctionalInterface
    public interface DriftChecker {
        boolean hasDrift(Path dblArtifact, Map<String, Object> frontmatter);
    }

    /**
     * Walk a DBL subdirectory; for each artifact, apply the drift checker; flip STALE on drift.
     *
     * @param subdir e.g., Path.of("SchemaIndex") or Path.of("APIIndex")
     * @param bridgeReport absolute path to the just-written Bridge report (used in the STALE marker text)
     * @param checker per-tool drift logic
     * @return list of DBL artifacts whose frontmatter was flipped to STALE
     */
    public List<Path> flipIfDrift(Path subdir, Path bridgeReport, DriftChecker checker) {
        List<Path> flipped = new ArrayList<>();
        Path scanRoot = dblRoot.resolve(subdir);
        if (!Files.isDirectory(scanRoot)) {
            LOG.debug("DBL subdir absent; stale-flip no-op for {}", scanRoot);
            return flipped;
        }

        try (Stream<Path> walk = Files.walk(scanRoot)) {
            walk.filter(Files::isRegularFile)
                .filter(p -> p.toString().endsWith(".md"))
                .forEach(artifact -> {
                    try {
                        Map<String, Object> fm = readFrontmatter(artifact);
                        if (fm == null) return;
                        Object lastRegen = fm.get("last_regenerated");
                        if (lastRegen instanceof String s && s.startsWith("STALE")) return;

                        if (checker.hasDrift(artifact, fm)) {
                            writeStaleMarker(artifact, fm, bridgeReport);
                            flipped.add(artifact);
                        }
                    } catch (Exception e) {
                        LOG.warn("Stale-flip check failed for {}: {}", artifact, e.getMessage());
                    }
                });
        } catch (IOException e) {
            LOG.warn("Walking DBL subdir {} failed: {}", scanRoot, e.getMessage());
        }
        return flipped;
    }

    /** Reads the YAML frontmatter from a Markdown file. Returns null if no frontmatter. */
    Map<String, Object> readFrontmatter(Path mdFile) throws IOException {
        String content = Files.readString(mdFile, StandardCharsets.UTF_8);
        if (!content.startsWith("---")) return null;
        Matcher m = BOUNDARY.matcher(content);
        if (!m.find()) return null;
        int yamlStart = m.end();
        if (!m.find()) return null;
        int yamlEnd = m.start();
        String yamlText = content.substring(yamlStart, yamlEnd);
        Object parsed = new Yaml().load(yamlText);
        if (parsed instanceof Map<?, ?> map) {
            Map<String, Object> result = new LinkedHashMap<>();
            for (Map.Entry<?, ?> e : map.entrySet()) {
                result.put(String.valueOf(e.getKey()), e.getValue());
            }
            return result;
        }
        return null;
    }

    /** Rewrites the artifact's frontmatter with last_regenerated set to STALE. */
    void writeStaleMarker(Path mdFile, Map<String, Object> fm, Path bridgeReport) throws IOException {
        String content = Files.readString(mdFile, StandardCharsets.UTF_8);
        Matcher m = BOUNDARY.matcher(content);
        if (!m.find()) return;
        if (!m.find()) return;
        int bodyStart = m.end();
        String body = content.substring(bodyStart);

        Map<String, Object> updated = new LinkedHashMap<>(fm);
        updated.put("last_regenerated",
                "STALE — superseded by AgentReports/Bridge/" + bridgeReport.getFileName());

        DumperOptions opts = new DumperOptions();
        opts.setDefaultFlowStyle(DumperOptions.FlowStyle.BLOCK);
        Yaml yaml = new Yaml(opts);
        String newYaml = yaml.dump(updated);

        Files.writeString(mdFile, "---\n" + newYaml + "---\n" + body, StandardCharsets.UTF_8);
        LOG.info("STALE-flipped DBL artifact: {}", mdFile);
    }
}
