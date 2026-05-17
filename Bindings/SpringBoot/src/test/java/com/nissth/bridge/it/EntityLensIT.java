package com.nissth.bridge.it;

import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.EntityLens;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Integration test for {@code entity_lens} per Phase_05 §3 Step 17.
 *
 * <p>Scans the fixture's {@code @Entity} classes and asserts the produced report
 * contains the live table summary. Also exercises the STALE-flip path: the
 * fixture's intentionally-stale {@code DBL/SchemaIndex/items.md} is copied into
 * a temp project root and the test asserts the copy's frontmatter is rewritten
 * with the {@code STALE — superseded by ...} marker (CLAUDE.md §11.4 risk-4
 * mitigation).
 *
 * <p>Drift detection: {@link EntityLens} compares the live schema to the
 * claimed schema by table-name AND per-table column set. The fixture's
 * {@code items.md} claims columns {@code id, name, description}; the real
 * {@code Item} entity has {@code id, name, qty}. The {@code description} vs
 * {@code qty} mismatch triggers the flip.
 */
class EntityLensIT {

    @Test
    void scans_fixture_entity_and_stale_flips_the_copied_DBL_artifact(@TempDir Path projectRoot) throws IOException {
        Path fixture = ItSupport.fixtureRoot();
        Path scanRoot = fixture.resolve("src/main/java");

        Path dblDir = projectRoot.resolve("DBL").resolve("SchemaIndex");
        Files.createDirectories(dblDir);
        Path dblItems = dblDir.resolve("items.md");
        String original = Files.readString(
                fixture.resolve("src/test/resources/DBL/SchemaIndex/items.md"));
        Files.writeString(dblItems, original);

        ReportWriter writer = new ReportWriter(projectRoot);
        EntityLens tool = new EntityLens(writer, new StaleFlipper(projectRoot));

        BridgeCommand cmd = new BridgeCommand(
                "entity_lens", null, null,
                new BridgeCommand.Scope(null, scanRoot.toString(), List.of(),
                        null, null, null, null, null, Map.of()),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);
        Path report = ((ToolResult.Success) result).reportPath();

        String body = Files.readString(report);
        assertThat(body)
                .as("must list the items table with its real columns")
                .contains("## Table: items")
                .contains("name")
                .contains("qty");

        String dblAfter = Files.readString(dblItems);
        assertThat(dblAfter)
                .as("STALE-flip must rewrite last_regenerated in the DBL copy")
                .contains("STALE")
                .contains("superseded by AgentReports/Bridge/")
                .contains(report.getFileName().toString());

        Set<ValidationMessage> errors = ItSupport.validateFrontmatter(report);
        assertThat(errors).as("entity_lens report frontmatter schema errors").isEmpty();
    }
}
