package com.nissth.bridge.tools;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.github.javaparser.ast.body.MethodDeclaration;
import com.github.javaparser.ast.body.Parameter;
import com.github.javaparser.ast.expr.AnnotationExpr;
import com.github.javaparser.ast.expr.MemberValuePair;
import com.github.javaparser.ast.expr.NormalAnnotationExpr;
import com.github.javaparser.ast.expr.SingleMemberAnnotationExpr;
import com.nissth.bridge.core.BridgeCommand;
import com.nissth.bridge.core.BridgeError;
import com.nissth.bridge.core.BridgeException;
import com.nissth.bridge.core.ReportContext;
import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import com.nissth.bridge.core.ToolHandler;
import com.nissth.bridge.core.ToolResult;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * endpoint_lens — diagnostic. AST-scans Java sources for {@code @RestController}
 * + {@code @*Mapping} annotations and emits an endpoint table. After write,
 * STALE-flips {@code DBL/APIIndex/*.md} artifacts whose claims drift.
 */
public class EndpointLens implements ToolHandler {

    public static final String NAME = "endpoint_lens";

    private static final Set<String> MAPPING_ANNOS = Set.of(
            "GetMapping", "PostMapping", "PutMapping", "DeleteMapping", "PatchMapping", "RequestMapping");
    private static final Set<String> AUTH_ANNOS = Set.of(
            "PreAuthorize", "Secured", "RolesAllowed", "PostAuthorize");
    private static final Pattern CLAIMED_ENDPOINT =
            Pattern.compile("^\\|\\s*(GET|POST|PUT|DELETE|PATCH)\\s*\\|\\s*([^\\s|]+)\\s*\\|",
                    Pattern.MULTILINE);

    private final ReportWriter writer;
    private final StaleFlipper flipper;
    private final JavaParser parser;

    public EndpointLens(ReportWriter writer, StaleFlipper flipper) {
        this.writer = writer;
        this.flipper = flipper;
        this.parser = new JavaParser(new ParserConfiguration()
                .setLanguageLevel(ParserConfiguration.LanguageLevel.JAVA_17));
    }

    @Override
    public String name() {
        return NAME;
    }

    @Override
    public ToolResult run(BridgeCommand cmd) {
        Path scanRoot = resolveScanRoot(cmd);
        String packageFilter = cmd.scope() == null ? null : cmd.scope().packageName();
        int maxDepth = (cmd.scope() == null || cmd.scope().maxDepth() == null)
                ? Integer.MAX_VALUE : cmd.scope().maxDepth();
        boolean withDto = "with_dto".equals(cmd.mode());

        List<Endpoint> endpoints = scan(scanRoot, packageFilter, maxDepth);

        ReportContext ctx = ReportContext.builder()
                .tool(NAME)
                .mode(cmd.mode() == null ? "default" : cmd.mode())
                .scope(cmd.scope())
                .generatedAt(OffsetDateTime.now())
                .contextId(cmd.contextId())
                .freshness(
                        "AST parse of *.java under " + scanRoot,
                        "javaparser walk at " + OffsetDateTime.now(),
                        "Live AST snapshot; reflects on-disk source as of read time")
                .body(renderBody(endpoints, scanRoot, withDto))
                .build();

        Path report = writer.write(ctx);

        // Stale-flip pass: check DBL/APIIndex/*.md
        Set<String> liveKeys = endpoints.stream()
                .map(e -> e.httpMethod() + " " + e.path())
                .collect(Collectors.toSet());
        flipper.flipIfDrift(Path.of("APIIndex"), report,
                (artifact, fm) -> {
                    if (!coversOverlaps(fm, scanRoot)) return false;
                    try {
                        Set<String> claimed = extractClaimedEndpoints(artifact);
                        return !claimed.equals(liveKeys);
                    } catch (IOException e) {
                        return false;
                    }
                });

        return new ToolResult.Success(report);
    }

    static Path resolveScanRoot(BridgeCommand cmd) {
        String rp = cmd.scope() == null ? null : cmd.scope().rootPath();
        if (rp == null || rp.isBlank()) {
            return Path.of("src", "main", "java").toAbsolutePath();
        }
        return Path.of(rp).toAbsolutePath();
    }

    List<Endpoint> scan(Path root, String packageFilter, int maxDepth) {
        if (!Files.isDirectory(root)) return List.of();
        List<Endpoint> out = new ArrayList<>();
        try (Stream<Path> walk = Files.walk(root, maxDepth)) {
            walk.filter(p -> p.toString().endsWith(".java"))
                    .forEach(file -> extractFromFile(file, packageFilter, out));
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(NAME,
                    "Failed to walk " + root + ": " + e.getMessage()));
        }
        return out;
    }

    void extractFromFile(Path file, String packageFilter, List<Endpoint> out) {
        try {
            String content = Files.readString(file, StandardCharsets.UTF_8);
            extractFromSource(content, packageFilter, file.toString(), out);
        } catch (IOException e) {
            // Skip unreadable file; tools shouldn't fail the whole scan on one bad file.
        }
    }

    /** Visible for tests: parse a source string directly. */
    void extractFromSource(String source, String packageFilter, String sourcePathForReport, List<Endpoint> out) {
        ParseResult<CompilationUnit> parsed = parser.parse(source);
        if (!parsed.isSuccessful() || parsed.getResult().isEmpty()) return;
        CompilationUnit cu = parsed.getResult().get();

        String pkg = cu.getPackageDeclaration().map(pd -> pd.getNameAsString()).orElse("");
        if (packageFilter != null && !packageFilter.isBlank() && !pkg.startsWith(packageFilter)) return;

        for (ClassOrInterfaceDeclaration cls : cu.findAll(ClassOrInterfaceDeclaration.class)) {
            boolean isController = cls.getAnnotations().stream()
                    .map(AnnotationExpr::getNameAsString)
                    .anyMatch(n -> n.equals("RestController") || n.equals("Controller"));
            if (!isController) continue;

            String fqn = pkg.isEmpty() ? cls.getNameAsString() : pkg + "." + cls.getNameAsString();
            String classPath = cls.getAnnotations().stream()
                    .filter(a -> a.getNameAsString().equals("RequestMapping"))
                    .findFirst()
                    .map(EndpointLens::extractPath)
                    .orElse("");

            for (MethodDeclaration m : cls.getMethods()) {
                for (AnnotationExpr a : m.getAnnotations()) {
                    String name = a.getNameAsString();
                    if (!MAPPING_ANNOS.contains(name)) continue;
                    String httpMethod = httpMethodFromAnno(a);
                    if (httpMethod == null) continue;
                    String methodPath = extractPath(a);
                    String combined = joinPaths(classPath, methodPath);

                    List<String> auth = m.getAnnotations().stream()
                            .map(AnnotationExpr::getNameAsString)
                            .filter(AUTH_ANNOS::contains)
                            .collect(Collectors.toList());
                    String requestDto = m.getParameters().stream()
                            .filter(p -> p.getAnnotations().stream()
                                    .anyMatch(an -> an.getNameAsString().equals("RequestBody")))
                            .map(Parameter::getTypeAsString)
                            .findFirst().orElse(null);
                    String responseDto = m.getType().asString();
                    String sig = m.getDeclarationAsString(false, false, false);

                    out.add(new Endpoint(httpMethod, combined, fqn, sig, requestDto, responseDto, auth, sourcePathForReport));
                }
            }
        }
    }

    static String httpMethodFromAnno(AnnotationExpr a) {
        return switch (a.getNameAsString()) {
            case "GetMapping" -> "GET";
            case "PostMapping" -> "POST";
            case "PutMapping" -> "PUT";
            case "DeleteMapping" -> "DELETE";
            case "PatchMapping" -> "PATCH";
            case "RequestMapping" -> extractMethodFromRequestMapping(a);
            default -> null;
        };
    }

    static String extractMethodFromRequestMapping(AnnotationExpr a) {
        if (a instanceof NormalAnnotationExpr n) {
            for (MemberValuePair p : n.getPairs()) {
                if (p.getNameAsString().equals("method")) {
                    String v = p.getValue().toString();
                    if (v.contains("GET")) return "GET";
                    if (v.contains("POST")) return "POST";
                    if (v.contains("PUT")) return "PUT";
                    if (v.contains("DELETE")) return "DELETE";
                    if (v.contains("PATCH")) return "PATCH";
                }
            }
        }
        return "GET"; // RequestMapping defaults to all methods; report as GET for simplicity
    }

    static String extractPath(AnnotationExpr a) {
        if (a instanceof SingleMemberAnnotationExpr s) {
            return unquote(s.getMemberValue().toString());
        }
        if (a instanceof NormalAnnotationExpr n) {
            for (MemberValuePair p : n.getPairs()) {
                String nm = p.getNameAsString();
                if (nm.equals("value") || nm.equals("path")) {
                    return unquote(p.getValue().toString());
                }
            }
        }
        return "";
    }

    static String unquote(String s) {
        if (s == null) return "";
        s = s.trim();
        if (s.startsWith("{") && s.endsWith("}")) {
            int comma = s.indexOf(',');
            String first = (comma > 0 ? s.substring(1, comma) : s.substring(1, s.length() - 1)).trim();
            return unquote(first);
        }
        if (s.length() >= 2 && s.startsWith("\"") && s.endsWith("\"")) return s.substring(1, s.length() - 1);
        return s;
    }

    static String joinPaths(String a, String b) {
        if (a == null || a.isEmpty()) return b == null ? "" : b;
        if (b == null || b.isEmpty()) return a;
        if (a.endsWith("/") && b.startsWith("/")) return a + b.substring(1);
        if (!a.endsWith("/") && !b.startsWith("/")) return a + "/" + b;
        return a + b;
    }

    static boolean coversOverlaps(java.util.Map<String, Object> fm, Path scanRoot) {
        Object covers = fm.get("covers");
        if (!(covers instanceof List<?> list)) return false;
        String rootStr = scanRoot.toString().replace('\\', '/');
        for (Object o : list) {
            if (!(o instanceof String s)) continue;
            String norm = s.replace('\\', '/');
            if (rootStr.contains(norm) || norm.contains(rootStr)) return true;
        }
        return false;
    }

    static Set<String> extractClaimedEndpoints(Path artifact) throws IOException {
        String content = Files.readString(artifact, StandardCharsets.UTF_8);
        Set<String> out = new HashSet<>();
        Matcher m = CLAIMED_ENDPOINT.matcher(content);
        while (m.find()) {
            out.add(m.group(1) + " " + m.group(2));
        }
        return out;
    }

    static String renderBody(List<Endpoint> endpoints, Path scanRoot, boolean withDto) {
        StringBuilder sb = new StringBuilder();
        sb.append("# endpoint_lens report\n\n");
        sb.append("- **Scan root:** `").append(scanRoot).append("`\n");
        sb.append("- **Endpoints found:** ").append(endpoints.size()).append("\n\n");
        if (endpoints.isEmpty()) {
            sb.append("_No `@RestController` / `@Controller` classes with mapping annotations under this scope._\n");
            return sb.toString();
        }
        if (withDto) {
            sb.append("| Method | Path | Controller | Signature | Request DTO | Response | Auth |\n");
            sb.append("|:---|:---|:---|:---|:---|:---|:---|\n");
            for (Endpoint e : endpoints) {
                sb.append("| ").append(e.httpMethod())
                        .append(" | `").append(e.path()).append("`")
                        .append(" | `").append(e.controller()).append("`")
                        .append(" | `").append(escape(e.methodSignature())).append("`")
                        .append(" | ").append(e.requestDto() == null ? "—" : "`" + e.requestDto() + "`")
                        .append(" | `").append(e.responseDto()).append("`")
                        .append(" | ").append(e.auth().isEmpty() ? "—" : String.join(", ", e.auth()))
                        .append(" |\n");
            }
        } else {
            sb.append("| Method | Path | Controller | Auth |\n");
            sb.append("|:---|:---|:---|:---|\n");
            for (Endpoint e : endpoints) {
                sb.append("| ").append(e.httpMethod())
                        .append(" | `").append(e.path()).append("`")
                        .append(" | `").append(e.controller()).append("`")
                        .append(" | ").append(e.auth().isEmpty() ? "—" : String.join(", ", e.auth()))
                        .append(" |\n");
            }
        }
        return sb.toString();
    }

    private static String escape(String s) {
        return s == null ? "" : s.replace("|", "\\|");
    }

    public record Endpoint(
            String httpMethod,
            String path,
            String controller,
            String methodSignature,
            String requestDto,
            String responseDto,
            List<String> auth,
            String sourceFile) {}
}
