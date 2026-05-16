package com.nissth.bridge.tools;

import com.nissth.bridge.core.ReportWriter;
import com.nissth.bridge.core.StaleFlipper;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class EndpointLensTest {

    private static final String CONTROLLER_SRC = """
            package com.example.api;

            import org.springframework.web.bind.annotation.*;
            import org.springframework.security.access.prepost.PreAuthorize;

            @RestController
            @RequestMapping("/api/items")
            public class ItemController {

                @GetMapping
                public List<ItemDto> list() {
                    return null;
                }

                @GetMapping("/{id}")
                public ItemDto get(@PathVariable Long id) {
                    return null;
                }

                @PostMapping(value = "/", consumes = "application/json")
                @PreAuthorize("hasRole('ADMIN')")
                public ItemDto create(@RequestBody CreateItemDto body) {
                    return null;
                }

                @DeleteMapping("/{id}")
                @Secured("ROLE_ADMIN")
                public void delete(@PathVariable Long id) {}
            }
            """;

    @Test
    void extracts_all_mappings_with_combined_paths(@TempDir Path repoRoot) {
        EndpointLens tool = new EndpointLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EndpointLens.Endpoint> out = new ArrayList<>();

        tool.extractFromSource(CONTROLLER_SRC, null, "synthetic", out);

        assertThat(out).hasSize(4);

        assertThat(out).extracting(EndpointLens.Endpoint::httpMethod)
                .containsExactly("GET", "GET", "POST", "DELETE");
        assertThat(out).extracting(EndpointLens.Endpoint::path)
                .containsExactly("/api/items", "/api/items/{id}", "/api/items/", "/api/items/{id}");
        assertThat(out.get(0).controller()).isEqualTo("com.example.api.ItemController");
    }

    @Test
    void captures_auth_and_request_dto(@TempDir Path repoRoot) {
        EndpointLens tool = new EndpointLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EndpointLens.Endpoint> out = new ArrayList<>();

        tool.extractFromSource(CONTROLLER_SRC, null, "synthetic", out);

        EndpointLens.Endpoint post = out.get(2);
        assertThat(post.requestDto()).isEqualTo("CreateItemDto");
        assertThat(post.auth()).containsExactly("PreAuthorize");

        EndpointLens.Endpoint del = out.get(3);
        assertThat(del.auth()).containsExactly("Secured");
    }

    @Test
    void package_filter_skips_unrelated_controllers(@TempDir Path repoRoot) {
        EndpointLens tool = new EndpointLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EndpointLens.Endpoint> out = new ArrayList<>();

        // Filter limits to a different package
        tool.extractFromSource(CONTROLLER_SRC, "com.other", "synthetic", out);

        assertThat(out).isEmpty();
    }

    @Test
    void non_controller_classes_are_ignored(@TempDir Path repoRoot) {
        EndpointLens tool = new EndpointLens(new ReportWriter(repoRoot), new StaleFlipper(repoRoot));
        List<EndpointLens.Endpoint> out = new ArrayList<>();

        String src = """
                package com.example;
                public class NotAController {
                    public void doSomething() {}
                }
                """;
        tool.extractFromSource(src, null, "synthetic", out);

        assertThat(out).isEmpty();
    }

    @Test
    void unquote_handles_array_form() {
        // @GetMapping({"/a", "/b"}) — take first path
        assertThat(EndpointLens.unquote("{\"/a\", \"/b\"}")).isEqualTo("/a");
    }

    @Test
    void joinPaths_handles_slash_normalization() {
        assertThat(EndpointLens.joinPaths("/api/items", "/{id}")).isEqualTo("/api/items/{id}");
        assertThat(EndpointLens.joinPaths("/api/items/", "/")).isEqualTo("/api/items/");
        assertThat(EndpointLens.joinPaths("/api", "items")).isEqualTo("/api/items");
        assertThat(EndpointLens.joinPaths("", "/items")).isEqualTo("/items");
    }
}
