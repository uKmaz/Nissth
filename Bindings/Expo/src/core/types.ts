export interface BridgeCommand {
  tool: string;
  mode?: string;
  context_id?: string;
  scope?: BridgeScope;
  output?: BridgeOutput;
}

export interface BridgeScope {
  package?: string;
  root_path?: string;
  names?: string[];
  file_extension?: string;
  tag_filter?: string;
  type_filter?: string;
  max_depth?: number;
  profile?: string;
  extra?: Record<string, unknown>;
}

export interface BridgeOutput {
  format?: "markdown" | "json" | "flat_text";
  destination?: "file" | "return" | "console";
  file_name?: string;
}

export type ErrorStage = "parse" | "validate" | "execute" | "format";

export interface BridgeErrorPayload {
  error: string;
  tool: string;
  stage: ErrorStage;
  error_code?: string;
  context_id?: string;
}

export interface ReportFreshness {
  source: string;
  source_state: string;
  guarantee: string;
}

export interface ReportFrontmatter {
  tool: string;
  mode?: string;
  binding: string;
  binding_version: string;
  generated_at: string;
  scope?: Record<string, unknown>;
  freshness: ReportFreshness;
  contract_version: 1;
}

export interface ReportContext {
  tool: string;
  mode?: string;
  scope?: Record<string, unknown>;
  freshness: ReportFreshness;
  body: string;
  fileName?: string;
}

export interface ToolResult {
  reportPath: string;
  body?: string;
}

export interface ToolHandler {
  readonly name: string;
  invoke(cmd: BridgeCommand): Promise<ToolResult>;
}

export interface BindingManifestEntry {
  name: string;
  kind: "diagnostic" | "action";
  modes: string[];
  scope_keys: string[];
  scope_extra_keys: string[];
  description: string;
  enforces?: string[];
}

export interface BindingManifestData {
  binding: string;
  binding_version: string;
  contract_version: 1;
  language: string;
  node_min: number;
  build_tool: string;
  description: string;
  tools: BindingManifestEntry[];
  scope_extra_keys_doc?: Record<string, Record<string, string>>;
}
