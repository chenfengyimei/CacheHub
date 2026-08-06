# Changelog

All notable changes to CacheHub will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0-prealpha] — 2026-08-07

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
