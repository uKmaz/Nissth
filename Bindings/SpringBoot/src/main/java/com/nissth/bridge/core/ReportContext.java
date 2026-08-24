package com.nissth.bridge.core;

import com.fasterxml.jackson.annotation.JsonProperty;

import java.time.OffsetDateTime;

/**
 * Inputs to ReportWriter. The frontmatter shape matches
 * Bindings/_schemas/bridge-command.schema.json $defs.reportFrontmatter
 * and CLAUDE.md §11.3.
 */
public record ReportContext(
        String tool,
        String mode,
        String binding,
        @JsonProperty("binding_version") String bindingVersion,
        @JsonProperty("generated_at") OffsetDateTime generatedAt,
        BridgeCommand.Scope scope,
        Freshness freshness,
        @JsonProperty("contract_version") int contractVersion,
        String body,
        @JsonProperty("context_id") String contextId
) {

    public ReportContext {
        if (generatedAt == null) generatedAt = OffsetDateTime.now();
        if (contractVersion == 0) contractVersion = 1;
    }

    /**
     * The freshness stamp that lets readers verify a report's data is current.
     * See CLAUDE.md §11.3.
     */
    public record Freshness(
            String source,
            @JsonProperty("source_state") String sourceState,
            String guarantee
    ) {}

    /** Builder for readability in tool implementations. */
    public static class Builder {
        private String tool;
        private String mode;
        private String binding = "spring-boot";
        private String bindingVersion = "0.1.0";
        private OffsetDateTime generatedAt;
        private BridgeCommand.Scope scope;
        private Freshness freshness;
        private int contractVersion = 1;
        private String body;
        private String contextId;

        public Builder tool(String s) { this.tool = s; return this; }
        public Builder mode(String s) { this.mode = s; return this; }
        public Builder binding(String s) { this.binding = s; return this; }
        public Builder bindingVersion(String s) { this.bindingVersion = s; return this; }
        public Builder generatedAt(OffsetDateTime t) { this.generatedAt = t; return this; }
        public Builder scope(BridgeCommand.Scope s) { this.scope = s; return this; }
        public Builder freshness(Freshness f) { this.freshness = f; return this; }
        public Builder freshness(String source, String sourceState, String guarantee) {
            this.freshness = new Freshness(source, sourceState, guarantee);
            return this;
        }
        public Builder body(String s) { this.body = s; return this; }
        public Builder contextId(String s) { this.contextId = s; return this; }

        public ReportContext build() {
            return new ReportContext(tool, mode, binding, bindingVersion, generatedAt, scope,
                    freshness, contractVersion, body, contextId);
        }
    }

    public static Builder builder() { return new Builder(); }
}
