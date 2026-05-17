package com.nissth.bridge.cli;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.nissth.bridge.core.BindingManifest;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeError;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.DefaultSubprocessRunner;
import com.nissth.bridge.core.JsonCommandParser;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import com.nissth.bridge.core.SubprocessRunner;
import com.nissth.bridge.core.ToolDispatcher;
import com.nissth.bridge.core.ToolHandler;
import com.nissth.bridge.core.ToolResult;
import com.nissth.bridge.tools.CompileVerify;
import com.nissth.bridge.tools.EndpointLens;
import com.nissth.bridge.tools.EntityFieldAdd;
import com.nissth.bridge.tools.EntityLens;
import com.nissth.bridge.tools.MigrationStatus;

import java.io.IOException;
import java.io.InputStream;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;

/**
 * Entry point per {@code CLAUDE.md} §11.5. Parses CLI args (flag form or {@code --json-stdin}),
 * dispatches via {@link ToolDispatcher}, prints the report path or body to stdout, and exits
 * with the §11.5 exit code (0/2/3/4/5).
 */
public class NissthBridgeCli {

    private final ToolDispatcher dispatcher;
    private final JsonCommandParser jsonParser;
    private final ObjectMapper mapper;

    /** Production constructor — wires all five handlers + default plumbing. */
    public NissthBridgeCli(Path repoRoot) {
        ReportWriter writer = new ReportWriter(repoRoot);
        StaleFlipper flipper = new StaleFlipper(repoRoot);
        SubprocessRunner runner = new DefaultSubprocessRunner();
        List<ToolHandler> handlers = List.of(
                new CompileVerify(runner, writer),
                new EndpointLens(writer, flipper),
                new EntityLens(writer, flipper),
                new MigrationStatus(runner, writer),
                new EntityFieldAdd(writer)
        );
        this.dispatcher = new ToolDispatcher(handlers);
        this.jsonParser = new JsonCommandParser();
        this.mapper = new ObjectMapper().enable(SerializationFeature.INDENT_OUTPUT);
    }

    /** Test constructor. */
    NissthBridgeCli(ToolDispatcher dispatcher, JsonCommandParser jsonParser) {
        this.dispatcher = dispatcher;
        this.jsonParser = jsonParser;
        this.mapper = new ObjectMapper().enable(SerializationFeature.INDENT_OUTPUT);
    }

    public static void main(String[] args) {
        Path repoRoot = resolveRepoRoot();
        PrintStream out = new PrintStream(System.out, true, StandardCharsets.UTF_8);
        PrintStream err = new PrintStream(System.err, true, StandardCharsets.UTF_8);
        int exit = new NissthBridgeCli(repoRoot).run(args, System.in, out, err);
        System.exit(exit);
    }

    static Path resolveRepoRoot() {
        String env = System.getenv("NISSTH_REPO_ROOT");
        if (env != null && !env.isBlank()) return Path.of(env).toAbsolutePath();
        return Path.of("").toAbsolutePath();
    }

    public int run(String[] args, InputStream in, PrintStream out, PrintStream err) {
        try {
            if (args.length == 0) {
                printUsage(err);
                return 2;
            }
            String first = args[0];
            return switch (first) {
                case "--help", "-h" -> {
                    printUsage(out);
                    yield 0;
                }
                case "--list-bindings" -> {
                    listBindings(out);
                    yield 0;
                }
                case "--list-tools" -> {
                    listTools(out, args);
                    yield 0;
                }
                case "--describe" -> describe(out, err, args);
                case "--json-stdin" -> runJsonStdin(in, out, err);
                default -> {
                    if (first.startsWith("--")) {
                        err.println("ERROR: unknown flag: " + first);
                        printUsage(err);
                        yield 2;
                    }
                    yield runFlagged(args, out, err);
                }
            };
        } catch (BridgeException e) {
            return writeError(err, e.error());
        } catch (Exception e) {
            err.println("ERROR: " + e.getClass().getSimpleName() + ": " + e.getMessage());
            return 3;
        }
    }

    // --- Discovery flags ----------------------------------------------------

    private void listBindings(PrintStream out) throws IOException {
        BindingManifest m = dispatcher.manifest();
        Map<String, Object> view = new LinkedHashMap<>();
        view.put("binding", m.binding());
        view.put("binding_version", m.bindingVersion());
        view.put("contract_version", m.contractVersion());
        view.put("language", m.language());
        view.put("build_tool", m.buildTool());
        view.put("description", m.description());
        view.put("tool_count", m.tools().size());
        out.println(mapper.writeValueAsString(view));
    }

    private void listTools(PrintStream out, String[] args) {
        String filter = stringValueAfter(args, "--binding");
        BindingManifest m = dispatcher.manifest();
        if (filter != null && !filter.equalsIgnoreCase(m.binding())) {
            // No matching binding installed — empty list, but not an error.
            return;
        }
        for (BindingManifest.ToolDescriptor td : m.tools()) {
            out.println(td.name() + "\t" + td.kind() + "\t" + safe(td.description()));
        }
    }

    private int describe(PrintStream out, PrintStream err, String[] args) throws IOException {
        if (args.length < 2 || args[1].startsWith("--")) {
            err.println("ERROR: --describe requires a tool name");
            return 2;
        }
        String tool = args[1];
        Optional<BindingManifest.ToolDescriptor> td = dispatcher.describe(tool);
        if (td.isEmpty()) {
            err.println("ERROR: unknown tool: " + tool);
            return 4;
        }
        out.println(mapper.writeValueAsString(td.get()));
        return 0;
    }

    // --- JSON stdin dispatch ------------------------------------------------

    private int runJsonStdin(InputStream in, PrintStream out, PrintStream err) {
        BridgeCommand cmd = jsonParser.parseStdin(in);
        return dispatchAndPrint(cmd, out, err);
    }

    // --- Flag-form dispatch -------------------------------------------------

    private int runFlagged(String[] args, PrintStream out, PrintStream err) {
        BridgeCommand cmd = parseFlagged(args);
        return dispatchAndPrint(cmd, out, err);
    }

    static BridgeCommand parseFlagged(String[] args) {
        String tool = args[0];
        String mode = null;
        String contextId = null;
        String packageName = null;
        String rootPath = null;
        List<String> names = new ArrayList<>();
        String fileExtension = null;
        String tagFilter = null;
        String typeFilter = null;
        Integer maxDepth = null;
        String profile = null;
        Map<String, Object> extra = new LinkedHashMap<>();
        BridgeCommand.Format format = BridgeCommand.Format.MARKDOWN;
        BridgeCommand.Destination destination = BridgeCommand.Destination.FILE;
        String fileName = null;

        for (int i = 1; i < args.length; i++) {
            String key = args[i];
            if (!key.startsWith("--")) {
                throw new BridgeException(BridgeError.parseError(tool,
                        "Unexpected positional arg: " + key));
            }
            String k = key.substring(2);
            // Discovery flags consumed here so help text reflects them; mid-command they are errors.
            String v = (i + 1 < args.length) ? args[i + 1] : null;
            if (v == null) {
                throw new BridgeException(BridgeError.parseError(tool,
                        "Flag '" + key + "' requires a value"));
            }
            i++;

            switch (k) {
                case "mode" -> mode = v;
                case "context-id", "context_id" -> contextId = v;
                case "scope.package" -> packageName = v;
                case "scope.root-path", "scope.root_path" -> rootPath = v;
                case "scope.names" -> {
                    for (String n : v.split(",")) {
                        if (!n.isBlank()) names.add(n.trim());
                    }
                }
                case "scope.file-extension", "scope.file_extension" -> fileExtension = v;
                case "scope.tag-filter", "scope.tag_filter" -> tagFilter = v;
                case "scope.type-filter", "scope.type_filter" -> typeFilter = v;
                case "scope.max-depth", "scope.max_depth" -> maxDepth = Integer.parseInt(v);
                case "scope.profile" -> profile = v;
                case "output.format" -> format = BridgeCommand.Format.fromJson(v);
                case "output.destination" -> destination = BridgeCommand.Destination.fromJson(v);
                case "output.file-name", "output.file_name" -> fileName = v;
                default -> {
                    if (k.startsWith("scope.extra.")) {
                        extra.put(k.substring("scope.extra.".length()), coerce(v));
                    } else {
                        throw new BridgeException(BridgeError.parseError(tool,
                                "Unknown flag: " + key));
                    }
                }
            }
        }

        BridgeCommand.Scope scope = new BridgeCommand.Scope(packageName, rootPath, names,
                fileExtension, tagFilter, typeFilter, maxDepth, profile, extra);
        BridgeCommand.Output output = new BridgeCommand.Output(format, destination, fileName);
        return new BridgeCommand(tool, mode, contextId, scope, output);
    }

    /** Coerce CLI string values to Boolean / Integer / String for {@code scope.extra}. */
    static Object coerce(String raw) {
        if ("true".equalsIgnoreCase(raw)) return Boolean.TRUE;
        if ("false".equalsIgnoreCase(raw)) return Boolean.FALSE;
        if (raw.matches("-?\\d+")) {
            try {
                return Integer.parseInt(raw);
            } catch (NumberFormatException e) {
                return raw;
            }
        }
        return raw;
    }

    // --- Result printing ----------------------------------------------------

    private int dispatchAndPrint(BridgeCommand cmd, PrintStream out, PrintStream err) {
        ToolResult result = dispatcher.dispatch(cmd);
        if (result instanceof ToolResult.Failure f) {
            return writeError(err, f.error());
        }
        ToolResult.Success s = (ToolResult.Success) result;
        BridgeCommand.Destination dest = cmd.output() == null
                ? BridgeCommand.Destination.FILE
                : cmd.output().destination();
        switch (dest) {
            case FILE -> out.println(s.reportPath().toAbsolutePath());
            case RETURN -> {
                String body = s.returnedBody();
                if (body == null) body = readSafely(s.reportPath());
                out.println(body);
            }
            case CONSOLE -> {
                out.println("Report: " + s.reportPath().toAbsolutePath());
                out.println();
                out.println(readSafely(s.reportPath()));
            }
        }
        return 0;
    }

    private static String readSafely(Path p) {
        try {
            return Files.readString(p, StandardCharsets.UTF_8);
        } catch (IOException e) {
            return "<could not read report: " + e.getMessage() + ">";
        }
    }

    private int writeError(PrintStream err, BridgeError error) {
        try {
            err.println(mapper.writeValueAsString(error));
        } catch (IOException e) {
            err.println("ERROR: " + error.stage() + " exit=" + error.exitCode()
                    + " error_code=" + safe(error.errorCode()) + " " + safe(error.error()));
        }
        return error.exitCode();
    }

    // --- Helpers ------------------------------------------------------------

    private static String stringValueAfter(String[] args, String flag) {
        for (int i = 0; i < args.length - 1; i++) {
            if (flag.equals(args[i])) return args[i + 1];
        }
        return null;
    }

    private static String safe(String s) {
        return s == null ? "" : s;
    }

    private static void printUsage(PrintStream s) {
        s.println("nissth-bridge — Diagnostic Bridge CLI (spring-boot binding)");
        s.println();
        s.println("usage:");
        s.println("  nissth-bridge <tool> [--mode <m>] [--scope.<key> <v>]... [--output.<key> <v>]...");
        s.println("  nissth-bridge --json-stdin                read one BridgeCommand JSON from stdin");
        s.println("  nissth-bridge --list-bindings             print this binding's manifest header (JSON)");
        s.println("  nissth-bridge --list-tools [--binding ID] print one tool per line: name<TAB>kind<TAB>description");
        s.println("  nissth-bridge --describe <tool>           print the tool's manifest descriptor (JSON)");
        s.println();
        s.println("scope flags:  --scope.package <pkg>  --scope.root-path <dir>  --scope.names <a,b,c>");
        s.println("              --scope.file-extension <.java>  --scope.tag-filter <t>  --scope.type-filter <t>");
        s.println("              --scope.max-depth <n>  --scope.profile <p>");
        s.println("              --scope.extra.<key> <v>  (binding-specific; bool/int auto-coerced)");
        s.println();
        s.println("output flags: --output.format markdown|json|flat_text");
        s.println("              --output.destination file|return|console");
        s.println("              --output.file-name <stem>");
        s.println();
        s.println("env:    NISSTH_REPO_ROOT=<path>   override the AgentReports/Bridge target root (default: cwd)");
        s.println("exit:   0 success  2 parse/validate  3 execute  4 unknown tool  5 freshness violation");
    }
}
