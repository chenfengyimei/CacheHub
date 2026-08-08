# Changelog

All notable changes to CacheHub will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0-prealpha] — 2026-08-08

### V7: Truth Closure & Benchmark Matrix (2026-08-08)

**"版本真实性"和"价值证据真实性"彻底做实。**

#### P0: Version-Aware Context Package
- **WorkspaceVersionFingerprint** (W01): GitStateProvider + Migration0011 + IndexSnapshot/Manifest/CacheKey 全链路绑定 git 指纹
- **Context Build stale detection** (W02): StaleDetector 比较当前 workspace fingerprint vs snapshot，防止"旧 Hash + 新 Content"
- **AgentBenchmark 多轮成功率修复** (W03): per-round success 替代累计 testsPassed/Total，新增 fail→pass 回归测试
- **DNS rebinding + launch nonce** (W04): Host 精确匹配 + auth/init 一次性 nonce

#### P1: Benchmark Framework Hardening
- **Benchmark patch protocol** (W05): unified diff system prompt + 失败轮错误反馈 + async timeout + RunsPerTask 生效
- **RecallWiringFactory + SecurityPolicy 指纹化** (W06): 统一 7 callback 组装 + SecurityPolicy.Version 改为真实 SHA256 指纹
- **Baseline 公平性** (W07): 移除 name.Contains("token") 误排源码 + relative path + 稳定排序
- **GUI/Stats/Provider/Docs/Release** (W08-W15): sync JSON 修复 + Provider URL 规范化 + Dashboard 真实 stats + Gateway 持久化 stats + CI 多平台 smoke

#### Benchmark Matrix (W16-W19)
- **13 个 fixture 仓库** (W16): TS/Python/Go/Rust/Monorepo × 70 文件，每个含真实源码 + 测试 + 有意 bug
- **25 个任务** (W17): 全部指向真实 fixture 路径，新增 RepositoryPath/TestCommand/TestCommandArgs 字段
- **BenchmarkMatrixRunner** (W18): `cachehub benchmark matrix` CLI 命令 + Phase Gate 评估
- **11 个测试** (W19): 矩阵运行 + Phase Gate 通过/失败场景

#### Continuous Hardening (W20-W23)
- **CI format 修复 + Windows publish-smoke 兼容** (W20): dotnet format 自动修复 + shell: bash
- **文档同步** (W21): 全部文档同步到 965 tests
- **ResolveFileHash 磁盘优先** (W22): 彻底消除"旧 Hash + 新 Content"不一致
- **publish-smoke 跨平台一致性** (W23): 全部步骤统一 shell: bash

### V6: Beta Closure & Evidence (2026-08-08)

Closing the gap between "module exists" and "module is on the production main chain."

#### P2: Parser Enhancements (Cross-Language Structural Accuracy)
- **Python method/function distinction**: Class indent stack tracks nested classes. `def` inside class body → `SymbolKind.Method`, module-level `def` → `SymbolKind.Function`.
- **TypeScript export default**: ExportRegex supports optional `default` keyword for `export default class/function/...`.
- **Relation persistence fidelity**: `CodeRelation` gains `SourceSymbol` and `Line` fields. `file_relations.source_symbol` now stores actual source symbol name (was relation type string). `line` now persisted (was always NULL).
- **Go parser v1.0**: Functions/methods (receiver distinction), import blocks, type declarations (struct/interface/type alias), const/var, interface embedding relations.
- **Rust parser v1.0**: Functions/methods (impl block distinction), use declarations, struct/enum/trait/impl declarations, implements relations, const/static.
- **Java parser v1.0**: class/interface/enum with extends/implements, imports (incl. static), methods with modifiers, fields/constants (static final → Constant).
- **C/C++ parser v1.0**: #include, #define, class/struct with inheritance, enum, namespace, typedef, function/method distinction (class body tracking).
- **PHP parser v1.0**: use statements (with alias), class/interface/trait/enum, function/method distinction, extends/implements, const declarations.
- **Language coverage**: 9 programming languages now have dedicated regex parsers (C#/TS/JS/Python/Go/Rust/Java/C/C++/PHP) + Markdown. Previously only 3 (C#/TS/Python).

#### V3 Remaining Items Fixed
- **Immutable snapshot for Refresh**: Refresh no longer modifies Active snapshot directly. Creates Building snapshot, clones all data, applies changes, then atomically switches Building→Active. If DB transaction fails, Building is cleaned up and Active remains untouched. FTS failures no longer corrupt Active state.
- **SqliteCacheStore wired into Gateway**: GatewayConfig.CacheStore property. GatewayServer uses ICacheStore for cache check/store with fallback to in-memory. GatewayCommands creates SqliteCacheStore with persistent cache.db + blob storage. Gateway cache survives restarts.
- **Benchmark CLI real ContextEngine**: Removed all DEMO ONLY warnings. `benchmark run` now accepts `--id=<workspace> --task=<task-id>`, creates real ContextEngine.Build, measures actual Recall@10 and TokenReduction. No longer uses Ground Truth as result.
- **Semantic Snapshot/ContentHash binding**: SemanticReference gains SnapshotId, WorkspaceContentHash, IsStale fields. PersistentVectorStore.InvalidateBySnapshot marks entries stale when snapshot changes. Search excludes stale entries. RecordAsync accepts snapshot/content hash for binding.

#### P0 Critical Fixes
- **file_relations mapping fix**: confidence/line/source columns were shifted — confidence held parser name, line held confidence double. Migration0010 adds `source` column and migrates mangled data.
- **Security exit enforcement unified**: CLI markdown export and FileExporter now pass SecurityPolicyEnforcer. All 3 export paths (Local API / CLI / File) enforce security policy.
- **README unsubstantiated claim removed**: "80%+ Token reduction" replaced with aspirational language.

#### P1 High-Priority Fixes
- **FTS BM25 rank + hit line**: Fts5Index.SearchAsync now returns `rank` (BM25 score) and actual hit line number. FtsSearchResult and FtsMatch carry new fields through the pipeline.
- **RankingEngine consumes ScoreHint**: TextMatch now takes the higher of path-based and FTS-based scores. SymbolMatch takes the higher of in-memory and hint-based scores.
- **FTS Anchor precision**: ExtractSnippetAnchors uses actual FTS hit line to generate ±20-line window anchors (was hardcoded 1/1).
- **Ignore rules unified**: ConsistencyReconciler accepts IgnoreRuleEngine. Refresh re-checks added/modified files against ignore rules. Newly-ignored modified files are removed from index.
- **Migration0009+0010 registered**: All 7 production migration runners now include Migration0009PersistentCache and Migration0010RelationSourceColumn.
- **CacheKey completed**: Added currentFile, gitDiff, modelId, tokenizerId to CacheKey.FullKey via ContextHash component.
- **ContextPackageCache wired**: CLI creates cache and passes to ContextEngine. Desktop DI registers cache singleton. Repeated context build calls now hit cache.
- **RelationRecallSource implemented**: New IRecallSource that queries file_relations for matched files, expands to target symbol definitions via SymbolSearch. RecallSource enum gains `Relation` value.
- **Contextual Completion entry**: `cachehub workflow completion` CLI command + `POST /api/v1/workflows/contextual-completion` API endpoint. Builds context → assembles prompt → returns system+user prompt.
- **Gateway multi-provider fallback**: GatewayConfig.FallbackProviders + GetAllProviders(). ForwardToProviderWithStatusAsync, HandleModelsAsync, HandleStreamingAsync, HandleResponsesAsync all support fallback on 429/5xx. /v1/models now includes Authorization header.
- **Semantic stable hash**: FNV-1a replaces string.GetHashCode() in LocalHashEmbeddingProvider. Vectors now stable across process restarts.
- **FTS failure tracking**: Refresh no longer silently swallows FTS errors. Failures tracked, reported to user, and logged to stderr.
- **Tokenizer full-chain fix**: Default tokenizer upgraded from CharEstimateTokenizer (chars/4) to CodeTokenizer. CreateWithDefaults() factory pre-registers GPT/Claude/Gemini/DeepSeek/Qwen models. ChunkingStrategy all private methods now use passed tokenizer instead of hardcoded EstimateTokens. SelectionEngine fallback paths (Metadata/Chunks/Outline) no longer drop tokenizer.
- **SemanticRecallSource wired**: New IRecallSource that queries semantic store for historical references similar to current task. Extracts mentioned file paths from reference content. Low-confidence reference signal (similarity*0.6). RecallPipeline.CreateDefaultSources now includes SemanticRecallSource.
- **Responses API streaming**: HandleResponsesAsync detects `stream: true` and uses ResponseHeadersRead mode for SSE passthrough. Non-streaming still uses full read.
- **Chat SSE Usage parsing**: New StreamAndParseUsageAsync method parses SSE `data:` lines for `usage` object during streaming. Prompt/completion tokens now tracked for streaming requests. Previously CopyToAsync didn't parse anything.
- **Documentation consistency**: AI_DEV_STATE.json has moduleStatus with accurate levels. Capabilities Limitations updated to match reality.

### Documentation & Quality Final Round (2026-08-07)

Final quality sweep: CI multi-OS, format compliance, doc alignment, ADR, and repomap fix.

#### CI
- Multi-OS matrix: ubuntu-latest + windows-latest + macos-latest with `fail-fast: false`
- Format check fix: 5 test files charset encoding corrected, IDE1006 suppressed for Gateway project

#### Documentation
- USAGE.md: port 5000→5099, Bearer Token auth in curl examples, `--provider-key` → environment variable
- AGENTS.md: project structure updated (Gateway project, 9 migrations, 772 tests)
- README: project structure includes CacheHub.Gateway, test count 772
- ARCHITECTURE.md: port 5099, 9 migrations, Gateway removed from Core layer, test count 772
- INSTALL.md: port 5099 + Bearer Token note
- integration/protocol/context-api.md: fully rewritten — 17 routes, Bearer auth, Gateway API, error code table
- AI_DEV_STATE.json: updated to V2.0-COMPLETE + AUDIT-62-RESOLVED
- ADR-0005: Gateway separation decision documented
- ADR-0004: stale port 5217→5099 fixed
- ADR README index: ADR-0004 and ADR-0005 added

#### Production Fixes
- install.ps1: added `-SkipTests` and `-Help` parameter parsing (matches install.sh)
- Desktop /export: repomap placeholder replaced with real directory tree generation (scans files, builds tree, language breakdown)
- .gitignore: broadened to cover `.codely-cli/auto-saves/` and transient files

#### Tests
- 8 new `SecurityGapTests`: symlink escape, XSS in content, offline mode, restricted mode, traversal variants
- 2 new `GatewayTests`: upstream error status code passthrough (401/429/500 not rewritten to 200), error responses not cached
- 1 new `SqliteDatabaseTests`: migration incremental upgrade v5→v9 (phase 1: migrate 1-5, verify; phase 2: migrate 6-9, verify)
- 1 new `RealBenchmarkTests`: R12 real benchmark measurement with actual Context Engine metrics

Test count: 761 → 772.

### Architecture Refactor (2026-08-07)

Closed the final 2 P2 architecture issues. All 62 audit issues now fully resolved (0 OPEN).

#### ARCH-P2-002: Indexing-Storage Dependency Cleanup
- Removed unused `CacheHub.Storage` project reference from `CacheHub.Indexing.csproj` (zero code-level dependency existed)
- Added direct `Storage` reference to `CacheHub.Cli.csproj` and `CacheHub.Context.csproj` (where Storage types are actually used)
- Dependency chain clarified: `Core ← Storage`, `Core ← Indexing` (no transitive Storage)

#### ARCH-P2-001: Gateway Separation from Core
- New `CacheHub.Gateway` project created (references only Core, no Storage/Context/Indexing)
- Moved 5 files from `Core/Gateway/` and `Core/Providers/` to `CacheHub.Gateway/`:
  - `GatewayModels.cs`, `Server/GatewayServer.cs`, `Streaming/SseStreamParser.cs`
  - `ProviderModels.cs`, `ProviderRouter.cs`
- Namespaces updated: `Core.Gateway` → `Gateway`, `Core.Providers` → `Gateway.Providers`
- CLI and Tests projects reference new `CacheHub.Gateway` project
- `.editorconfig` updated with Gateway analyzer suppressions
- Core now contains only domain models, contracts, and small scaffold modules (Semantic, LSP)

### Final P1 Gap Closure (2026-08-07)

Closed the last 4 PARTIALLY FIXED P1 issues. All 22 P1 issues now fully resolved (0 OPEN, 0 PARTIAL).

#### Production Fixes
- **IDX-P1-002**: `ConsistencyReconciler.IsModified` now computes real SHA-256 content hash when size+mtime match, detecting same-size mutations
- **IDX-P1-003**: `IgnoreRuleEngine` uses `PathComparer.PhysicalPathComparison` (platform-aware case sensitivity) instead of hardcoded `OrdinalIgnoreCase`
- **IDX-P1-005**: `RefreshAsync` rewritten to three-phase batch transaction (collect → single-transaction write → separate FTS), matching `BuildAsync` pattern
- **CTX-P1-001**: `SelectionEngine.Select` and `ChunkingStrategy.Chunk` accept optional `ITokenizer?`, using real token counting when available (fallback to chars/4)

#### Tests
- 2 new Reconciler tests: same-size-same-mtime mutation detection via content hash, unchanged-when-hash-matches

Test count: 759 → 761.

### P1 Gap Fix Round (2026-08-07)

Second audit-driven round: verified all 22 P1 issues (13 FIXED, 8 PARTIALLY FIXED, 0 OPEN) and closed the remaining gaps with production fixes and real verification tests.

#### Verification
- Confirmed production CLI (`ContextCommands.GetActiveSnapshotAsync`) and Desktop (`GetActiveSnapshotIdAsync`) bind real Active Snapshot — CTX-P0-001 FIXED
- Confirmed `ResolveFileHash` computes real SHA-256 from disk (CLI + Desktop) — CTX-P0-002 FIXED
- Confirmed Provider/Router/Budget real implementations — PROV-P1-001 FIXED

#### Real-Data Verification Tests
- `RealPipelineIntegrationTests`: added budget assertions (`payload.TotalEstimatedTokens <= ContextHardLimit`), phantom-file exclusion (`payload paths ⊆ manifest paths`), and empty-content checks
- `RealBenchmarkTests.R12_RealBenchmark_MeasuresActualMetricsAgainstGateThresholds`: replaces mock assertions with real Context Engine runs over 3 tasks, measuring actual Recall@10, MissingContext, and TokenReduction vs full-repo baseline

#### Production Fixes
- **SEC-P1-002**: `PathNormalizer.IsWithinRoot` now uses `PathComparer.PhysicalPathComparison` (platform-aware case sensitivity)
- **API-P1-002**: Gateway error responses (401/500/413) unified to `ErrorEnvelope`; added `ErrorCode.AuthRequired` and `RequestTooLarge`
- **API-P1-001**: CLI Capability declaration aligned with Desktop (adds ContextFeedback/Cache/Gateway); Desktop adds `"error": 1` schema; stale Limitations removed
- **CTX-P1-010**: `ContextEngine.Build` now accepts optional `ContextPackageCache` and checks/stores cache across calls

#### Tests
- 6 new `AuditFixVerificationTests` (hash computation, expand revision persistence, payload security, batch transaction parser persistence)
- 3 new `ContextCacheIntegrationTests` (cache hit on identical request, miss on different task, no-cache default)
- 1 new real benchmark measurement test
- Enhanced E2E budget/phantom-file assertions

Test count: 749 → 759.

### Audit Fix Round (2026-08-07)

Comprehensive audit gap analysis and fix round based on `CacheHub_全面审计与改进策划案_V1.0`.

#### Install & CI
- `install.sh`: abort on test failure (exit 1), add `--skip-tests` danger flag (matches `install.ps1`)

#### Index Integrity
- Desktop API `/workspaces/{id}/index`: rewritten to use batch transaction + parser persistence (symbols/imports/relations), matching CLI pattern
- FTS indexing separated to independent transaction (FTS5 virtual table limitation)
- Snapshot activation uses independent transaction for workspace-scoped atomic switch

#### Context Engine
- `/expand` endpoint: now calls `ContextExpander.CreateRevision` and persists child package to DB (ParentPackageId + cumulative budget)
- `ResolveFileHash` (CLI + Desktop): computes real SHA-256 from disk when DB hash is `pending` or `fp:` fingerprint, instead of returning `sha256:pending`
- `/payload` endpoint: pre-pass security evaluation returns `blockedFiles` list with `approvalRequired` and `reason` for each blocked file

#### Documentation
- README: full maturity matrix update — 20+ items upgraded from Experimental to Implemented
- README: security, Gateway, indexing, recall, chunking, budget, and expand descriptions corrected to match actual code state
- Test count updated: 379 → 755
- Port corrected: 5000 → 5099

#### Tests
- 6 new `AuditFixVerificationTests`: hash computation, expand revision persistence, payload security, batch transaction parser persistence

### Security Fixes (R0)
- Local API: loopback-only binding + random access token + Host header validation
- SafePathResolver: workspace-scoped path resolution, symlink escape detection
- Frontend XSS: removed all innerHTML, added CSP, esc() helper for all dynamic content
- Gateway: bearer token auth, real upstream status codes (no fixed 200), only cache 2xx
- Gateway: SingleFlight uses ConcurrentDictionary<string, Lazy<T>> (atomic), body size limit
- SecurityPolicyEnforcer: PolicyDecision (Allow/Deny/ApprovalRequired) at Payload exit
- Snapshot activation: fixed cross-workspace SQL (added workspace_id filter + transaction)
- Context Build: binds real Active Snapshot, reads real file hash (not "pending")
- Benchmark: marked as DEMO, cannot produce phase gate passed

### Indexing & Persistence (R1)
- PhysicalPath/VirtualPath/PathComparer: platform-aware path model
- Migration0006: files mtime/hash_kind/parser_version + file_symbols/file_imports/file_relations tables
- Migration0007: context_packages repository_commit/branch/dirty_state_hash/extracted_symbols_json/parser_versions_json/parent_package_id
- Index build: single-connection batch transaction, parser results persisted
- Index refresh: incremental update (add/modify/delete)
- gitignore matcher: !, **, root anchor, last-rule-wins
- Reconcile: VirtualPath + size+mtime comparison, same-size mutation detection
- FTS QueryCompiler: safe escaping + prefix matching
- RecallPipeline: FTS and symbol search callbacks
- Context Repository: complete round-trip (all Manifest fields persisted)

### Context Engine (R2)
- TaskParser v2: Unicode/Chinese support, bigram keywords, 4 identifier styles (PascalCase/camelCase/snake_case/kebab-case)
- Ranking v2: only implemented features (7), removed zero-dimension, added SizeEfficiency
- TokenBudget v2: AgentReserved + ResponseReserved in formula (MaxAvailable = window - all reserved)
- Chunking v2: anchor-based (LineAnchor), only relevant chunks
- PayloadPlan: immutable, Manifest/Payload share same plan
- RepoMap v2: real directory tree, importance scoring, budget pruning
- Context Expand: symbol support via file_symbols table query
- Context Explain: complete FeatureBreakdown with weighted contributions

### Protocol & GUI (R3)
- ErrorEnvelope: unified across CLI and Desktop API
- Git clone: --no-lfs replaced with GIT_LFS_SKIP_SMUDGE, credential redaction, output limit
- File Export: Plan/Apply separation, backup, atomic .gitignore update
- Feedback: validates Context/Workspace, --id must match JSON ContextPackageId
- Project Detection: .csproj SDK-based framework detection (no guessing)
- Desktop API: index build endpoint for GUI closed loop
- Selection: ghost file (0-token) exclusion
- Workspace: CreateValidated for real imports (directory existence check)
- FileWatcher: Renamed saves old+new path, queue max 10000
- Gateway: API key only from environment variable (not CLI args)
- FileHasher: layered hashing (full SHA-256 for small, fingerprint for large)
- FileEntry state machine: Discovered/Indexed/Ignored/Failed/Deleted/Stale
- FTS5 index: version-aware full-text search bound to IndexSnapshotId
- RipgrepSearcher: process wrapper + fallback search, results marked with SearchSource
- FileWatcher: debounced event queue + overflow detection
- ConsistencyReconciler: disk vs index comparison, Git HEAD change detection

#### Parsing
- ICodeParser contract: symbols, imports, call expressions, relations, diagnostics
- CSharpRegexParser: namespace/class/method/property/import + heuristic calls
- TypeScriptRegexParser: export class/interface/function/import + calls
- PythonRegexParser: class/def/import/decorator + calls
- TextParser + MarkdownParser: headings, code blocks, config keys
- CodeRelation: syntactic/heuristic/semantic with 0..1 confidence
- DeterministicOutlineGenerator: stable sorted outline by line + name
- RepoMapGenerator: budget-limited tree with key symbols
- ParserCache: hash + ParserId + version keyed cache

#### Security
- SecurityPolicyEnforcer: 4-level exfiltration mode (Standard/Restricted/PreviewRequired/Offline)
- SecretScanner: API key, password, private key, connection string, bearer token detection
- DefaultSensitivePatterns: .env, .pem, .key, id_rsa, credentials.json, etc.
- CheckBeforeSend: combined path + content + mode check

#### Gateway
- GatewayServer: HttpListener loopback, OpenAI-compatible forwarding
- CacheSafetyChecker: rejects tools, high temperature, streaming, no-cache
- SingleFlight: concurrent request deduplication for safe requests
- SseStreamParser: OpenAI-compatible SSE parsing (delta/finish_reason/usage)
- GatewayStats: requests, cache hit rate, token savings, avg latency

#### Providers
- IProvider contract, OpenAiCompatibleProvider baseline
- ModelInfo with capabilities and versioned pricing
- CostCalculator: input/output/cached cost calculation
- CredentialRef: credential ID only, never stores secrets
- RoutingConfig: Explicit/RoundRobin/LeastLatency/Fallback
- BudgetLimit + UsageRecord: per-workspace/provider limits and audit

#### Other Modules
- TokenizerRegistry: 3 tokenizers (char-estimate, word-boundary, code) + model registry
- ProjectDetectionEngine: 9 ecosystem detectors (Node/Python/.NET/Go/Rust/Java/Unity/Flutter/Docker)
- InMemoryVectorStore: cosine similarity search + workspace-scoped deletion
- LSP: ILanguageServer contract, LspLifecycle with auto-restart, JSON-RPC 2.0 serializer
- LruCache: thread-safe LRU + TTL + size limit + per-type stats
- ConfigManager: .cachehub-config.json load/save
- GitDiffProvider: changed file detection + HEAD commit hash
- GitProcessWrapper: parameter-array execution, clone/status/diff/ff-only-pull
- Ecosystem: PluginManifest, EnterprisePolicy, TeamConfig, UpdateConfig

#### CLI (21 commands, 55 subcommands)
- capabilities, workspace, index, context, detect, gateway, config, stats, repo, version
- explain, search, benchmark, outline, repomap, clean, token, hash, help, integration

#### Local API (17 routes)
- capabilities, workspaces CRUD, context (build/inspect/list/export/expand/feedback/explain/payload)
- search, outline, stats

#### Desktop UI (6 pages)
- Workspaces, Context (with Explain/Expand), History, Search, Integration, Settings

#### Benchmark
- 6 cross-language benchmark tasks with Ground Truth (Required/Helpful/Distractor)
- MetricsCalculator: Recall@K, Precision, aggregation, phase gate evaluation
- ReportGenerator: JSON report with config/summary/phase gate/tasks/failures/limitations
- FailureAttribution: Retrieval/Ranking/Budget/AgentNonCompliance/ModelRandom/Environment

#### Persistence
- 5 SQLite migrations: Initial (workspaces/snapshots/files/jobs), FTS5, ContextPackages, Feedback (context_feedback + feedback_files), ContextPackageDetails (budget details + JSON payload columns)
- SqliteWorkspaceRepository, SqliteContextPackageRepository, SqliteFeedbackRepository

#### Infrastructure
- MIT License
- GitHub Actions CI (restore/build/test/format)
- Single-file self-contained publish + install scripts (PowerShell/Bash)
- AGENTS.md, system-prompt-snippet.md, 3 Agent examples (Codex/Claude Code/Shell)
- 4 ADRs, Integration Kit protocol docs
- .cachehub-config.json configuration system

### Test Coverage
- 379 unit tests + 2 skipped (require git environment) = 381 total
- 8 end-to-end integration tests
- 5 real-world scenario tests

## [0.1.0] — 2026-08-06

### Added
- Initial project structure (.NET 9, 6 projects)
- Core protocol freeze: Context Package Schema v1, Capability Discovery v1, Error model
- Strong-typed identifiers (WorkspaceId, FileId, IndexSnapshotId, etc.)
- AI development state mechanism (AI_DEV_STATE.json, ROADMAP_STATUS.md, ADRs)
- Project development plan V3.0 and AI execution manual V1.0
