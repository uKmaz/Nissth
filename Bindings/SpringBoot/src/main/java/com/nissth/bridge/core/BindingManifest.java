package com.nissth.bridge.core;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;

import java.util.List;

/**
 * Parsed spring-boot.bridge.json — the tool registration manifest.
 * The schema for this file is per-binding (not part of the cross-stack contract).
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record BindingManifest(
        String binding,
        @JsonProperty("binding_version") String bindingVersion,
        @JsonProperty("contract_version") int contractVersion,
        String language,
        @JsonProperty("jvm_min") Integer jvmMin,
        @JsonProperty("build_tool") String buildTool,
        String description,
        List<ToolDescriptor> tools
) {

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record ToolDescriptor(
            String name,
            String kind,
            List<String> modes,
            @JsonProperty("scope_keys") List<String> scopeKeys,
            @JsonProperty("scope_extra_keys") List<String> scopeExtraKeys,
            String description,
            List<String> enforces
    ) {
        public ToolDescriptor {
            if (modes == null) modes = List.of();
            if (scopeKeys == null) scopeKeys = List.of();
            if (scopeExtraKeys == null) scopeExtraKeys = List.of();
            if (enforces == null) enforces = List.of();
        }

        public boolean isAction() {
            return "action".equalsIgnoreCase(kind);
        }
    }
}
