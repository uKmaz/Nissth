package com.nissth.bridge.it;

import com.networknt.schema.ValidationMessage;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.DefaultSubprocessRunner;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.CompileVerify;
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
 * Integration test for {@code compile_verify} per Phase_05 §3 Step 17.
 *
 * <p>Runs the real Maven subprocess against the on-disk fixture project at
 * {@code Bindings/SpringBoot/tests/fixture/}. Asserts the produced Bridge
 * report has status CLEAN and that its frontmatter conforms to the schema.
 */
class CompileVerifyIT {

    @Test
    void clean_fixture_compiles_and_report_says_CLEAN(@TempDir Path reportRoot) throws IOException {
        Path fixture = ItSupport.fixtureRoot();
        ReportWriter writer = new ReportWriter(reportRoot);
        CompileVerify tool = new CompileVerify(new DefaultSubprocessRunner(), writer);

        BridgeCommand cmd = new BridgeCommand(
                "compile_verify", null, null,
                new BridgeCommand.Scope(null, fixture.toString(), List.of(),
                        null, null, null, null, null, Map.of()),
                BridgeCommand.Output.defaults());

        ToolResult result = tool.run(cmd);
        assertThat(result).isInstanceOf(ToolResult.Success.class);

        Path report = ((ToolResult.Success) result).reportPath();
        assertThat(report).exists().isRegularFile();

        String body = Files.readString(report);
        assertThat(body)
                .as("clean fixture compile must report CLEAN")
                .contains("**Status:** CLEAN");

        Set<ValidationMessage> errors = ItSupport.validateFrontmatter(report);
        assertThat(errors).as("compile_verify report frontmatter schema errors").isEmpty();
    }
}
