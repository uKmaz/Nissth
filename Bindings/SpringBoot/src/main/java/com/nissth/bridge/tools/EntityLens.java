package com.nissth.bridge.tools;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParseResult;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.ClassOrInterfaceDeclaration;
import com.github.javaparser.ast.body.FieldDeclaration;
import com.github.javaparser.ast.body.VariableDeclarator;
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
import java.util.Map;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * entity_lens — diagnostic. AST-scans Java sources for {@code @Entity} classes
 * and emits a table summary. After write, STALE-flips {@code DBL/SchemaIndex/*.md}
 * artifacts whose schema claim drifts from the live scan.
 */
public class EntityLens implements ToolHandler {

    public static final String NAME = "entity_lens";

    private static final Set<String> RELATIONSHIP_ANNOS = Set.of(
            "OneToOne", "OneToMany", "ManyToOne", "ManyToMany");
    private static final Pattern CLAIMED_TABLE =
            Pattern.compile("^##\\s*Table:\\s*([A-Za-z0-9_]+)", Pattern.MULTILINE);

    private final ReportWriter writer;
    private final StaleFlipper flipper;
    private final JavaParser parser;

    public EntityLens(ReportWriter writer, StaleFlipper flipper) {
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
        boolean withRelationships = "with_relationships".equals(cmd.mode());

        List<Entity> entities = scan(scanRoot, packageFilter, maxDepth);

        ReportContext ctx = ReportContext.builder()
                .tool(NAME)
                .mode(cmd.mode() == null ? "default" : cmd.mode())
                .scope(cmd.scope())
                .generatedAt(OffsetDateTime.now())
                .contextId(cmd.contextId())
                .freshness(
                        "AST parse of *.java under " + scanRoot,
                        "javaparser walk at " + OffsetDateTime.now(),
                        "Live AST snapshot of @Entity classes")
                .body(renderBody(entities, scanRoot, withRelationships))
                .build();

        Path report = writer.write(ctx);

        Set<String> liveTables = entities.stream()
                .map(Entity::tableName)
                .collect(Collectors.toSet());
        flipper.flipIfDrift(Path.of("SchemaIndex"), report,
                (artifact, fm) -> {
                    if (!EndpointLens.coversOverlaps(fm, scanRoot)) return false;
                    try {
                        Set<String> claimed = extractClaimedTables(artifact);
                        return !claimed.equals(liveTables);
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

    List<Entity> scan(Path root, String packageFilter, int maxDepth) {
        if (!Files.isDirectory(root)) return List.of();
        List<Entity> out = new ArrayList<>();
        try (Stream<Path> walk = Files.walk(root, maxDepth)) {
            walk.filter(p -> p.toString().endsWith(".java"))
                    .forEach(file -> extractFromFile(file, packageFilter, out));
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(NAME,
                    "Failed to walk " + root + ": " + e.getMessage()));
        }
        return out;
    }

    void extractFromFile(Path file, String packageFilter, List<Entity> out) {
        try {
            String content = Files.readString(file, StandardCharsets.UTF_8);
            extractFromSource(content, packageFilter, file.toString(), out);
        } catch (IOException e) {
            // Skip unreadable file.
        }
    }

    /** Visible for tests: parse a source string directly. */
    void extractFromSource(String source, String packageFilter, String sourcePathForReport, List<Entity> out) {
        ParseResult<CompilationUnit> parsed = parser.parse(source);
        if (!parsed.isSuccessful() || parsed.getResult().isEmpty()) return;
        CompilationUnit cu = parsed.getResult().get();
        String pkg = cu.getPackageDeclaration().map(pd -> pd.getNameAsString()).orElse("");
        if (packageFilter != null && !packageFilter.isBlank() && !pkg.startsWith(packageFilter)) return;

        for (ClassOrInterfaceDeclaration cls : cu.findAll(ClassOrInterfaceDeclaration.class)) {
            boolean isEntity = cls.getAnnotations().stream()
                    .map(AnnotationExpr::getNameAsString)
                    .anyMatch(n -> n.equals("Entity"));
            if (!isEntity) continue;

            String fqn = pkg.isEmpty() ? cls.getNameAsString() : pkg + "." + cls.getNameAsString();
            String tableName = cls.getAnnotations().stream()
                    .filter(a -> a.getNameAsString().equals("Table"))
                    .findFirst()
                    .map(a -> attrValue(a, "name"))
                    .filter(s -> s != null && !s.isEmpty())
                    .orElse(cls.getNameAsString().toLowerCase());

            List<Column> columns = new ArrayList<>();
            List<Relationship> relationships = new ArrayList<>();

            for (FieldDeclaration f : cls.getFields()) {
                List<String> annoNames = f.getAnnotations().stream()
                        .map(AnnotationExpr::getNameAsString)
                        .collect(Collectors.toList());
                boolean isId = annoNames.contains("Id");
                String columnName = f.getAnnotations().stream()
                        .filter(a -> a.getNameAsString().equals("Column"))
                        .findFirst()
                        .map(a -> attrValue(a, "name"))
                        .orElse(null);
                String nullable = f.getAnnotations().stream()
                        .filter(a -> a.getNameAsString().equals("Column"))
                        .findFirst()
                        .map(a -> attrValue(a, "nullable"))
                        .orElse(null);

                String relAnno = annoNames.stream().filter(RELATIONSHIP_ANNOS::contains).findFirst().orElse(null);
                if (relAnno != null) {
                    for (VariableDeclarator vd : f.getVariables()) {
                        String mappedBy = f.getAnnotations().stream()
                                .filter(a -> a.getNameAsString().equals(relAnno))
                                .findFirst()
                                .map(a -> attrValue(a, "mappedBy"))
                                .orElse(null);
                        relationships.add(new Relationship(
                                vd.getNameAsString(),
                                vd.getTypeAsString(),
                                relAnno,
                                mappedBy == null,
                                mappedBy));
                    }
                    continue;
                }

                for (VariableDeclarator vd : f.getVariables()) {
                    String javaType = vd.getTypeAsString();
                    String col = columnName != null ? columnName : toSnakeCase(vd.getNameAsString());
                    columns.add(new Column(
                            col,
                            javaType,
                            !"false".equals(nullable),
                            isId));
                }
            }

            out.add(new Entity(fqn, tableName, columns, relationships, sourcePathForReport));
        }
    }

    static String attrValue(AnnotationExpr a, String key) {
        if (a instanceof SingleMemberAnnotationExpr s && "value".equals(key)) {
            return EndpointLens.unquote(s.getMemberValue().toString());
        }
        if (a instanceof NormalAnnotationExpr n) {
            for (MemberValuePair p : n.getPairs()) {
                if (p.getNameAsString().equals(key)) {
                    return EndpointLens.unquote(p.getValue().toString());
                }
            }
        }
        return null;
    }

    static String toSnakeCase(String camel) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < camel.length(); i++) {
            char c = camel.charAt(i);
            if (Character.isUpperCase(c) && i > 0) sb.append('_');
            sb.append(Character.toLowerCase(c));
        }
        return sb.toString();
    }

    static Set<String> extractClaimedTables(Path artifact) throws IOException {
        String content = Files.readString(artifact, StandardCharsets.UTF_8);
        Set<String> out = new HashSet<>();
        Matcher m = CLAIMED_TABLE.matcher(content);
        while (m.find()) out.add(m.group(1));
        return out;
    }

    static String renderBody(List<Entity> entities, Path scanRoot, boolean withRelationships) {
        StringBuilder sb = new StringBuilder();
        sb.append("# entity_lens report\n\n");
        sb.append("- **Scan root:** `").append(scanRoot).append("`\n");
        sb.append("- **Entities found:** ").append(entities.size()).append("\n\n");
        if (entities.isEmpty()) {
            sb.append("_No `@Entity` classes under this scope._\n");
            return sb.toString();
        }
        for (Entity e : entities) {
            sb.append("## Table: ").append(e.tableName()).append("\n\n");
            sb.append("- **Class:** `").append(e.fqn()).append("`\n\n");
            sb.append("| Column | Java type | Nullable | PK |\n");
            sb.append("|:---|:---|:---:|:---:|\n");
            for (Column c : e.columns()) {
                sb.append("| ").append(c.columnName())
                        .append(" | `").append(c.javaType()).append("`")
                        .append(" | ").append(c.nullable() ? "yes" : "no")
                        .append(" | ").append(c.primaryKey() ? "yes" : "—")
                        .append(" |\n");
            }
            if (withRelationships && !e.relationships().isEmpty()) {
                sb.append("\n**Relationships:**\n\n");
                sb.append("| Field | Type | Kind | Owning | mappedBy |\n");
                sb.append("|:---|:---|:---|:---:|:---|\n");
                for (Relationship r : e.relationships()) {
                    sb.append("| ").append(r.field())
                            .append(" | `").append(r.targetType()).append("`")
                            .append(" | ").append(r.kind())
                            .append(" | ").append(r.owning() ? "yes" : "no")
                            .append(" | ").append(r.mappedBy() == null ? "—" : r.mappedBy())
                            .append(" |\n");
                }
            }
            sb.append("\n");
        }
        return sb.toString();
    }

    public record Entity(String fqn, String tableName, List<Column> columns,
                         List<Relationship> relationships, String sourceFile) {}

    public record Column(String columnName, String javaType, boolean nullable, boolean primaryKey) {}

    public record Relationship(String field, String targetType, String kind,
                               boolean owning, String mappedBy) {}
}
