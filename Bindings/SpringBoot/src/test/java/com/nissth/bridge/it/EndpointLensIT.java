package com.nissth.bridge.it;

import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.EndpointLens;
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
 * Integration test for {@code endpoint_lens} per Phase_05 §3 Step 17.
 *
 * <p>AST-scans the fixture's {@code src/main/java/com/example/fixture/}, asserts
 * the produced report lists exactly one endpoint
 * ({@code GET /api/items} from {@code ItemController#list}) and that frontmatter
 * conforms to the schema.
 */
class EndpointLensIT {

    @Test
    void scans_fixture_controller_and_lists_the_single_GET_endpoint(@TempDir Path reportRoot) throws IOException {
        Path fixture = ItSupport.fixtureRoot();
        Path scanRoot = fixture.resolve("src/main/java");
        ReportWriter writer = new ReportWriter(reportRoot);
        EndpointLens tool = new EndpointLens(writer, new StaleFlipper(reportRoot));

        BridgeCommand cmd = new BridgeCommand(
                "endpoint_lens", null, null,
                new BridgeCommand.Scope(null, scanRoot.toString(), List.of(),
                        null, null, null, null, null, Map.of()),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);

        Path report = ((ToolResult.Success) result).reportPath();
        String body = Files.readString(report);
        assertThat(body)
                .as("must list ItemController's GET /api/items")
                .contains("**Endpoints found:** 1")
                .contains("| GET ")
                .contains("`/api/items`")
                .contains("ItemController");

        Set<ValidationMessage> errors = ItSupport.validateFrontmatter(report);
        assertThat(errors).as("endpoint_lens report frontmatter schema errors").isEmpty();
    }
}
