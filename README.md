# Portion AI — Local Resume Search & Interactive Screening Engine

Portion AI is a fully local, air-gapped recruitment copilot. It ingests resumes from disk, extracts
and chunks their text, embeds every chunk with a local LLM, and answers recruiter questions by
retrieving the most semantically similar resume passages and streaming a grounded, cited answer.

Nothing leaves the machine. The only external dependencies are a local PostgreSQL (optional) and a
local [Ollama](https://ollama.com) runtime.

- **Backend** — ASP.NET Core 10, 4-project Clean Architecture solution, EF Core 10, pgvector / SQLite
- **Frontend** — React 19, TypeScript 6, Vite 8, react-router 7
- **AI** — Ollama (`nomic-embed-text` for embeddings, a chat model for answers)

---

## Table of contents

1. [Why it is built this way](#1-why-it-is-built-this-way)
2. [Repository layout](#2-repository-layout)
3. [Backend architecture](#3-backend-architecture)
4. [Frontend architecture](#4-frontend-architecture)
5. [How it works under the hood](#5-how-it-works-under-the-hood)
6. [HTTP API reference](#6-http-api-reference)
7. [SSE event contract](#7-sse-event-contract)
8. [Configuration reference](#8-configuration-reference)
9. [Getting started](#9-getting-started)
10. [Testing](#10-testing)
11. [Design decisions worth knowing](#11-design-decisions-worth-knowing)
12. [Known gaps and pending work](#12-known-gaps-and-pending-work)

---

## 1. Why it is built this way

The original codebase was two files: one 138-line MVC controller that injected `ApplicationDbContext`
directly, and a 554-line React component that did its own `fetch` calls, held every piece of state,
and rendered invented analytics. It worked, but it had no seams: the document parser was a static
`switch`, the chunker was a private method on a `BackgroundService`, errors were hand-rolled JSON,
and the UI's "match score" and skill bars were produced by hashing the candidate's *name* into
pseudo-random numbers.

The restructure replaced that with the structure below. The functional surface is unchanged — same
dual-provider persistence, same bounded-channel ingestion, same retrieval cascade, same streaming
chat — but every capability now sits behind an interface, is covered by tests, and can be swapped
without touching its callers.

The single most important consequence: **no fabricated data**. The old dashboard drew a radar chart
of skills and a 0–100 match badge that were derived from a string hash of the candidate's name. Those
charts now render the actual cosine-similarity scores returned by the vector search.

---

## 2. Repository layout

```
Portion/
├── Portion.sln                     Solution: 4 src projects + 3 test projects
├── Directory.Build.props           Shared compiler settings (net10.0, nullable, deterministic)
├── Directory.Packages.props        Central Package Management — every version lives here
├── start-all.ps1                   Boot script: PostgreSQL → Ollama → API → UI
├── README.md                       This file
│
├── src/
│   ├── Portion.Domain/             Pure business model. No framework, no I/O.
│   ├── Portion.Application/        Use cases + pure logic. Depends only on Domain.
│   ├── Portion.Infrastructure/     All I/O: EF Core, Ollama, file system, queues, workers.
│   └── Portion.Api/                The only executable. HTTP surface + composition root.
│
├── tests/
│   ├── Portion.Domain.Tests/       Result/Error, Guard, PagedResult, FileHash
│   ├── Portion.Application.Tests/  Chunker, vector math, prompt builder, retrieval cascade, handlers
│   └── Portion.Api.Tests/          WebApplicationFactory integration tests
│
├── Portion.Server/                 Legacy compatibility host — see §12
│
└── portion-ui/                     React 19 + Vite SPA
```

### Dependency rule

```
Portion.Api  ──►  Portion.Infrastructure  ──►  Portion.Application  ──►  Portion.Domain
     └─────────────────►  Portion.Application  ────────────────────────────►
```

Dependencies point inward only. `Portion.Domain` and `Portion.Application` have **zero** references
to ASP.NET Core, EF Core, Ollama, or the file system. That is what makes the chunker, the retrieval
cascade, and the screening orchestrator unit-testable with plain fakes and no database.

### Where the composition root is

`PortionApiHost.ConfigureServices` in `src/Portion.Api/PortionApiHost.cs` calls three extension
methods, in this order:

| Call | File | Responsibility |
|---|---|---|
| `AddPortionApi` | `src/Portion.Api/Infrastructure/ApiServiceCollectionExtensions.cs` | ProblemDetails, exception handlers, JSON, CORS, rate limiting, multipart limits, API options |
| `AddPortionInfrastructure` | `src/Portion.Infrastructure/DependencyInjection.cs` | EF Core, repositories, Ollama clients, extractors, queues, hosted workers, health checks, options validation |
| `AddPortionOpenApi` | `src/Portion.Api/Infrastructure/PortionOpenApi.cs` | Swashbuckle document + SwaggerUI |

Every `TryAdd*` registration in `DependencyInjection.cs` uses `TryAdd`, so a test can substitute a
fake for any single service without rebuilding the container.

---

## 3. Backend architecture

### `Portion.Domain` — the business model

Zero dependencies except `Pgvector` (for the `Vector` type on the embedding).

```
Common/     Result, Result<T>, Error, ErrorType, PagedResult<T>, Guard
Entities/   Resume, ResumeChunk
Enums/      IngestionStatus (Discovered | Processing | Synced | Failed)
ValueObjects/FileHash
```

Errors are returned, not thrown, for expected outcomes:

```csharp
Error.Validation("resume.not-found", "No resume exists with that id.")
Error.NotFound("resume.not-found", "No resume exists with that id.")
```

`ValidationException` and `NotFoundException` exist only for crossing layer boundaries where a
`Result` cannot be propagated; three `IExceptionHandler` implementations in the API layer turn them
into RFC 7807 responses.

### `Portion.Application` — use cases and pure logic

```
Abstractions/    IApplicationDbContext, IUnitOfWork, ITextChunker, IResumeSearchService,
                 IScreeningService, IVectorChunkSearch, IEmbeddingGenerator,
                 IChatCompletionStreamer, IIngestionQueue, IResumeIngestionProcessor,
                 IRepositorySyncService, ISyncJobTracker, IFileStorage, IUploadPolicy,
                 IHashCalculator, IClock, IDocumentTextExtractor(+Factory)
Contracts/       ResumeContracts, ScreeningContracts (records shared with the API layer)
Features/
  Resumes/Queries/   ListResumesQuery, GetResumeQuery
  Resumes/Commands/  UploadResumeCommand, DeleteResumeCommand
  Sync/              ScheduleRepositorySyncCommand, GetSyncJobQuery
Services/        ScreeningService, ResumeSearchService, TextChunker, VectorMath,
                 PromptBuilder, ResumeFileName
```

**Vertical slices.** Each feature folder holds a request record, a result type, and a handler class
(`ListResumesQueryHandler`, `UploadResumeCommandHandler`, …) rather than a monolithic service with
one method per operation. Adding an endpoint means adding one file to one feature folder, not editing
a service and a controller.

`IClock` exists so the pipeline is deterministic under test; without it, every timestamp assertion
would need tolerance.

### `Portion.Infrastructure` — all I/O

```
Persistence/     ApplicationDbContext, Configurations/{Resume,ResumeChunk}Configuration.cs,
                 DatabaseInitializer, DesignTimeDbContextFactory
Repositories/    ResumeRepository
Retrieval/       ProviderAwareVectorChunkSearch
Sourcing/        DirectoryRepositorySyncService
Ai/              OllamaHttpClient, OllamaEmbeddingGenerator, OllamaChatCompletionStreamer
Documents/       PdfTextExtractor, DocxTextExtractor, PlainTextExtractor,
                 DocumentTextExtractorFactory
Storage/         FileSystemResumeStorage, StorageLayout, DataRootResolver
Ingestion/       ChannelIngestionQueue, ChannelSyncJobQueue, ResumeIngestionWorker,
                 ResumeIngestionProcessor, RepositorySyncWorker
Sync/            InMemorySyncJobTracker
Health/          DbHealthCheck, OllamaHealthCheck
Configuration/   OllamaOptions, IngestionOptions, StorageOptions, OptionsUploadPolicy
```

**Document extraction is a strategy, not a `switch`.** Each format implements
`IDocumentTextExtractor` (registered as a singleton implementing the same interface) and
`DocumentTextExtractorFactory` resolves by extension. Adding `.rtf` means adding one class and one
registration — the `switch` would have meant editing a utility used by the worker.

**One provider branch point.** `ProviderAwareVectorChunkSearch` is the only place in the codebase that
knows whether the backing store can rank vectors in SQL. The application layer asks
`IVectorChunkSearch.FindNearestAsync(embedding, topK)` and gets back `ScoredChunk`s either way.

### `Portion.Api` — the HTTP surface

```
Program.cs / PortionApiHost.cs
Endpoints/       ResumeEndpoints, ScreeningEndpoints, SyncEndpoints, HealthEndpoints
Contracts/       HttpContracts (HTTP-only request/response records)
Configuration/   CorsOptions, RateLimitPolicies, ScreeningOptions
Infrastructure/  GlobalExceptionHandler, ValidationExceptionHandler, NotFoundExceptionHandler,
                 PortionProblemDetailsFactory, SseWriter, RequestLoggingMiddleware,
                 RateLimiting, PortionJson, PortionOpenApi, PortionActivitySource
```

Endpoints are **minimal APIs** grouped by feature with `MapGroup`, each carrying `.WithName()`,
`.WithSummary()`, `.WithDescription()` and explicit `.Produces*` declarations so the generated
OpenAPI document is complete without a single XML doc comment.

### Middleware pipeline order

Defined in `PortionApiHost.ConfigurePipeline`:

| # | Middleware | Why it sits here |
|---|---|---|
| 1 | `UseExceptionHandler` | First, so it wraps everything and no fault escapes as a bare 500 |
| 2 | `UseHsts` + `UseHttpsRedirection` | Skipped in Development — HSTS on `localhost` persists a strict transport policy and breaks browser testing |
| 3 | `UseResponseCompression` | Brotli + gzip, with `text/event-stream` **excluded** from the MIME list |
| 4 | `UseForwardedHeaders` | `ForwardLimit = 2`; known-proxies cleared so a containerised reverse proxy is honoured |
| 5 | `UseCors` | Explicit origins from config; credentials disabled |
| 6 | `UseSwagger` / `UseSwaggerUI` | Development only |
| 7 | `RequestLoggingMiddleware` | Structured request/response logging with a trace identifier |
| 8 | `Map*Endpoints()` | |

Two details that are easy to get wrong and are called out in comments at the call site:

- **`text/event-stream` must not be compressed.** A compressing middleware or proxy buffers the
  response, so the first LLM token would be withheld until the entire answer had been generated —
  which destroys the entire point of streaming.
- **`AddResponseCompression`, not `Configure<ResponseCompressionOptions>`.** The middleware resolves
  `IResponseCompressionProvider` from the container, and only the `Add…` overload registers the
  provider services. The `Configure…` form compiles, passes review, and crashes at startup.

---

## 4. Frontend architecture

```
portion-ui/
├── index.html                     Inline pre-paint theme script (no dark-mode flash)
├── vite.config.ts                 Dev+preview proxy to :5000, manual vendor chunks, sourcemaps
├── .env.example                   VITE_API_BASE_URL, VITE_POLL_INTERVAL_MS
└── src/
    ├── main.tsx                   Bootstrap only: StrictMode → ErrorBoundary → Theme → Toast → Router
    ├── app/
    │   ├── App.tsx                Shell: header, nav, <Outlet/>, toast host
    │   ├── router.tsx             createBrowserRouter
    │   └── providers/             ThemeProvider (localStorage + prefers-color-scheme), ToastProvider
    ├── components/
    │   ├── layout/                AppHeader, AppNav
    │   ├── ui/                    Panel, Button, StatusBadge, EmptyState, Spinner, TextField
    │   ├── candidate/             CandidateCard, MatchScoreChart
    │   └── ErrorBoundary.tsx
    ├── features/
    │   ├── screening/             ScreeningPage + 7 components + useScreeningStream
    │   ├── resumes/               ResumeRegistryPage, ResumeTable, useResumeRegistry
    │   ├── upload/                UploadResumePage, useResumeUpload
    │   └── sync/                  SyncRepositoryPage, useSyncJob
    ├── services/                  httpClient, resumeApi, syncApi, screeningApi
    ├── hooks/                     usePolling, useAsync, useClickOutside, useIsMounted
    ├── lib/                       cn, formatDate, formatFileSize, formatScore, initials
    ├── config/env.ts              Typed, defaulted import.meta.env access
    ├── types/api.ts               Types mirroring the backend contract exactly
    └── styles/                    tokens, base, layout, components, features/*
```

**Routes:** `/` → `/screening`, plus `/resumes`, `/upload`, `/sync`, and a `*` catch-all that
redirects to `/screening`.

**The API layer is the only place that knows the wire format.** `httpClient.ts` owns the base URL,
query building, JSON/FormData/void bodies, per-request timeouts, composed abort signals, and
RFC 7807 parsing into a typed `ApiError`. No component contains a URL string, and no `fetch` call
appears outside `services/`.

**Errors are never swallowed.** Every call routes its failure into a toast or an inline message. The
old code had `catch { /* silent */ }` in three places, which meant a dead backend looked identical
to an empty database.

**Polling is conditional.** `usePolling` refuses overlapping runs and skips while the tab is hidden.
The resume registry only auto-refreshes while at least one row on the current page is in `Processing`
state — the previous version polled every 5 seconds on every route forever.

**Theme persists.** `ThemeProvider` writes to `localStorage`, falls back to `prefers-color-scheme` on
first run, and `index.html` runs a tiny inline script before React mounts so a dark-mode reload does
not flash white.

### Design tokens

`src/styles/tokens.css` holds the entire palette as CSS custom properties, defined once for
`[data-theme="dark"]` and once for `[data-theme="light"]`. Switching themes is a single
`data-theme` attribute on `<html>`; no component knows which theme is active.

---

## 5. How it works under the hood

### 5.1 Direct resume upload

```
Browser                Api                     Application              Infrastructure        Db
   │                     │                            │                         │                 │
   │ POST /api/v1/recruitment/resumes (multipart)     │                         │                 │
   ├────────────────────►│                            │                         │                 │
   │                     │ UploadResumeCommandHandler │                         │                 │
   │                     ├───────────────────────────►│                         │                 │
   │                     │                            │ IUploadPolicy.Validate │                 │
   │                     │                            ├────────────────────────►│                 │
   │                     │                            │ IFileStorage.Write      │                 │
   │                     │                            ├────────────────────────►│  disk          │
   │                     │                            │ IHashCalculator.Sha256  │                 │
   │                     │                            ├────────────────────────►│                 │
   │                     │                            │ ResumeRepository.ExistsByHash            │
   │                     │                            ├────────────────────────────────────────►│
   │                     │                            │                         │                 │
   │                     │            ┌── already Synced? ── yes ──► delete temp file,
   │                     │            │                        return 202 alreadyIndexed
   │                     │            │ no
   │                     │            ▼
   │                     │            │ ResumeRepository.Add(Processing)              │
   │                     │            ├────────────────────────────────────────►│
   │                     │            │ IIngestionQueue.Enqueue                      │
   │                     │            ├───────────────────────────────►│  bounded Channel(200)
   │ 202 Accepted ───────┤◄───────────┴────────────────────────────────────────────┘
   │  { resumeId, alreadyIndexed:false }
```

The request returns as soon as the row exists. Everything expensive — parsing, chunking, embedding —
happens off the request thread. The client's only feedback channel is the registry endpoint, which
shows `Processing` → `Synced`.

### 5.2 The ingestion worker

`ResumeIngestionWorker` is a `BackgroundService` and the single owner of ingestion execution.

```
Channel<IngestionRequest>(capacity 200, SingleReader=true, FullMode=Wait)
        │
        ▼  await foreach
   create a fresh DI scope per item
        │
        ▼  IResumeIngestionProcessor.ProcessAsync
   ┌────────────────────────────────────────────────────────────────┐
   │ 1. Load Resume, set Status = Processing                        │
   │ 2. DocumentTextExtractorFactory.Create(path) → text           │
   │ 3. TextChunker.Chunk(text, 500 words, 50-word overlap)        │
   │ 4. For each chunk: IEmbeddingGenerator.GenerateAsync         │
   │ 5. DELETE existing chunks (re-index safety), INSERT new ones  │
   │ 6. Status = Synced, LastSyncedAt = now                        │
   └────────────────────────────────────────────────────────────────┘
        │
        ├─ success → log info, keep draining
        └─ failure → Status = Failed + FailureReason, keep draining
```

Three things matter here:

- **A DI scope per item.** `ApplicationDbContext` is scoped. Resolving it once in the worker would
  share tracked entities across every document processed for the lifetime of the process.
- **Existing chunks are deleted before new ones are inserted.** Without this, re-ingesting an edited
  resume would append a second copy of every chunk and silently double every search result.
- **Shutdown drains.** `StopAsync` calls `IIngestionQueue.Complete()` then waits up to 30 s for the
  in-flight document. A mid-flight cancellation is converted to a `Failed` row rather than losing the
  document.

### 5.3 Bulk folder reconciliation

```
POST /api/v1/recruitment/sync { folderPath }
   → ScheduleRepositorySyncCommandHandler
   → ISyncJobQueue.Enqueue(folderPath)
   → 202 { jobId }

RepositorySyncWorker drains the queue
   → ISyncJobTracker.MarkRunning
   → Directory.EnumerateFiles(path, "*", AllDirectories)
   → filter by Ingestion:SupportedExtensions
   → for each file:
        SHA-256 → look up existing row by hash
          found & Synced  → skipped++
          found & not     → Status = Processing, enqueue → queued++
          not found       → INSERT (Discovered), enqueue → queued++
   → ISyncJobTracker.MarkCompleted(discovered, skipped, queued, failed)

GET /api/v1/recruitment/sync/{jobId}  → the UI polls until Completed or Failed
```

The pre-refactor code called `Task.Run(() => ...)` from the controller action and bound the work to
`HttpContext.RequestAborted` — so the job silently died the moment the HTTP response was written.
Now the request only enqueues, and the work belongs to a hosted service.

`ISyncJobTracker` is an in-memory `ConcurrentDictionary`. That is a deliberate, documented limitation
— job history does not survive a restart (see §12).

### 5.4 A screening question, end to end

This is the most interesting path. `GET /api/v1/screening/stream?prompt=...` returns a Server-Sent
Events stream, and the *order* of events is a documented public contract.

```
status:embedding
   │  ScreeningService.ExecuteAsync is a C# async iterator
   ▼  IEmbeddingGenerator.GenerateAsync(query)
   │  → POST {Ollama}/api/embeddings { model: nomic-embed-text, prompt }
   │  → float[768]
   │  On failure: log a warning and continue with embedding = null
   │               (a missing embedding is expected when Ollama is offline)
   ▼
status:search
   │  ResumeSearchService.RetrieveAsync — the cascade, in order:
   │
   │  ① vector    IVectorChunkSearch.FindNearestAsync(embedding, topK)
   │                ├─ Postgres: OrderBy(c => c.Embedding.CosineDistance(v))
   │                │            translates to the pgvector `<=>` operator.
   │                │            Index-assisted; the corpus never enters the app.
   │                └─ SQLite:   project the vectors out, rank in memory with
   │                             VectorMath.SimilarityScore. Correct, but O(n) —
   │                             the documented zero-config trade-off.
   │                score = round(1 − cosineDistance, 4)
   │
   │  ② keyword   EF.Functions.Like(TextContent, "%query%")
   │              LIKE metacharacters in the query are escaped, so a literal
   │              '%' or '_' cannot widen the pattern.
   │              These get a *decayed* score ≤ 0.5 — no embedding comparison
   │              happened, so a high number would be a lie.
   │
   │  ③ first     Take(topK) of the newest chunks, so the model still has
   │     available  something grounded. Same decayed scoring.
   │
   │  ④ none      Empty corpus → RetrievalMode.None
   ▼
status:fallback          (emitted only when ② or ③ was used — the UI shows it)
   │
   │  matches { topChunkCount, candidates:[{ resumeId, candidateName, score, chunkCount }] }
   │    → this is what the match panel and the bar chart render
   ▼
status:generating
   │  PromptBuilder.SystemPrompt  (fixed, grounding rules: use only the supplied
   │                              context, say so when data is missing, answer
   │                              in markdown)
   │  PromptBuilder.BuildUserPrompt(groundedContext, query)
   ▼
token *                   IChatCompletionStreamer.StreamAsync
   │  → POST {Ollama}/api/chat { stream: true }
   │  → newline-delimited JSON, read line by line, deserialised incrementally
   │  → SseWriter flushes after every token
   ▼
status:complete
done { elapsedMs }

   on downstream failure →  error { message }  then close
   on client disconnect   →  log at information level, write nothing
```

**The retrieval cascade never throws on an empty corpus.** `RetrievalMode.None` is a first-class
result, and `ScreeningService` substitutes a fixed "no resumes are currently indexed" string as the
grounded context. The model is never asked to answer with no context at all.

**Why a C# async iterator cannot `try/catch` around `yield`.** C# forbids `yield return` inside a
`try` block that has a `catch` clause. That single language rule shaped the error design: a
recoverable embedding outage is caught *before* the iterator yields anything, and any other fault is
allowed to propagate so the transport can emit a terminal `error` frame. Once the response has
committed to `200`, an error frame is the only honest thing left to send.

**Why validation happens before the stream opens.** A blank prompt, or one over 8 000 characters, is
returned as a real `400 application/problem+json` — checked before a single SSE byte is written. An
error *inside* a committed `200` stream would be indistinguishable from an answer.

### 5.5 How the browser consumes the stream

```
screeningApi.openScreeningStream(prompt, handlers, { topK })
   → new EventSource('/api/v1/screening/stream?prompt=…&topK=5')
   → addEventListener('status'  | 'matches' | 'token' | 'done' | 'error', …)
        │
        ▼
useScreeningStream  (features/screening/hooks/)
   │  status  → setStage()      → PipelineStatus line
   │  matches → setMatches()    → MatchPanel + MatchScoreChart + CandidateCard
   │  token   → append to the in-flight assistant message
   │  done    → setElapsedMs(), clear isStreaming, stream.close()
   │  error   → toast + inline text, clear isStreaming, stream.close()
   │
   │  close() also fires on: unmount, explicit cancel, and a new prompt
   │  superseding an in-flight one. streamRef is a ref, so a re-render never
   │  orphans an open connection.
```

`EventSource` is used rather than `fetch` + a reader because the browser handles reconnection and
buffering for us, and the protocol is one-directional. The stream is explicitly closed in every
terminal path — a leaked `EventSource` pins an HTTP connection and a server-side generator.

---

## 6. HTTP API reference

Base path: `/api/v1`. All errors are RFC 7807 `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/recruitment/resumes",
  "traceId": "00-8f2c…-00",
  "errors": { "pageSize": ["PageSize must be between 1 and 200."] }
}
```

`IngestionStatus` serialises as a string: `Discovered` | `Processing` | `Synced` | `Failed`.

### Resumes

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/recruitment/resumes?page=1&pageSize=25&q=` | Paged registry. `q` matches candidate name, case-insensitive. `pageSize` max 200. |
| `GET` | `/api/v1/recruitment/resumes/{id}` | One resume plus its chunks. `404` if unknown. |
| `POST` | `/api/v1/recruitment/resumes` | `multipart/form-data`, field name **`file`**. → `202` |
| `DELETE` | `/api/v1/recruitment/resumes/{id}?deleteFile=true` | → `204`. Cascades to chunks. |

```jsonc
// GET /api/v1/recruitment/resumes
{
  "items": [{
    "id": "b024f95f-…", "candidateName": "Rakeshkumar",
    "fileName": "Rakeshkumar.pdf", "filePath": "…/UploadedResumes/…pdf",
    "status": "Synced", "failureReason": null,
    "createdAt": "2026-09-27T18:04:11Z", "lastSyncedAt": "2026-09-27T18:04:14Z",
    "chunkCount": 12, "embeddedChunkCount": 12
  }],
  "page": 1, "pageSize": 25, "totalCount": 5, "totalPages": 1
}
```

`fileName` and `embeddedChunkCount` are computed, not stored. Adding columns would have broken the
existing local SQLite database, which has no migration history.

Upload responses:

```jsonc
{ "message": "Resume queued for ingestion.", "resumeId": "…", "alreadyIndexed": false }
{ "message": "Resume is already indexed.",   "resumeId": "…", "alreadyIndexed": true  }
```

Upload errors: `400` empty file or unsupported extension, `413` over `Ingestion:MaxUploadBytes`.

### Sync

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/recruitment/sync` | `{ "folderPath": "C:\\Resumes" }` → `202 { message, jobId }` |
| `GET` | `/api/v1/recruitment/sync/{jobId}` | Job progress. `404` if unknown. |

```jsonc
{
  "jobId": "9a1f…", "folderPath": "C:\\Resumes",
  "state": "Running",           // Queued | Running | Completed | Failed
  "discovered": 40, "skipped": 35, "queued": 5, "failed": 0,
  "startedAt": "2026-09-27T18:10:02Z", "completedAt": null, "error": null
}
```

### Screening

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/screening/stream?prompt=…&topK=5` | SSE stream. `400` if the prompt is blank or > 8 000 chars, `429` under rate limiting. |

### Health & docs

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/health` | Aggregate report. `200` even when Ollama is down. |
| `GET` | `/health/live` | `self` tag — process liveness only. |
| `GET` | `/health/ready` | `ready` tag — database + Ollama. |
| `GET` | `/swagger` | SwaggerUI. **Development only.** |

**Ollama is reported `Degraded`, not `Unhealthy`, on purpose.** An unavailable local model degrades
retrieval to keyword matching, but the API still serves traffic. Failing readiness would pull a
working instance out of the load-balancer rotation over a recoverable condition.

---

## 7. SSE event contract

`Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`,
`X-Accel-Buffering: no` (that last one stops nginx from buffering the stream).

| Event | `data` payload | Emitted |
|---|---|---|
| `status` | `{"stage":"embedding"\|"search"\|"fallback"\|"generating"\|"complete"}` | Throughout |
| `matches` | `{"topChunkCount":5,"candidates":[{"resumeId":"…","candidateName":"…","score":0.8734,"chunkCount":2}]}` | Once, after retrieval |
| `token` | JSON-escaped single-line string | Zero or more |
| `done` | `{"elapsedMs":1234}` | Once, terminal |
| `error` | `{"message":"…"}` | At most once, terminal |

Guaranteed order:

```
status:embedding → status:search → [status:fallback] → matches
                 → status:generating → token* → status:complete → done
```

A `token` payload is always one line — newlines are escaped by JSON encoding, and the client
decodes. The previous implementation hand-escaped `\n` and prefixed status text into the data field
as `[STATUS] …`, which forced the browser to string-match its way through a protocol it already had.

---

## 8. Configuration reference

`src/Portion.Api/appsettings.json`. Every options type is bound with `.ValidateDataAnnotations()` and
`.ValidateOnStart()`, so a misconfigured deployment **fails at startup** with an actionable message
rather than at the first request that happens to need the setting.

```jsonc
{
  "DatabaseProvider": "Sqlite",          // or "PostgreSQL"
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=portion_local.db"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:1.5b",             // chat model
    "EmbeddingModel": "nomic-embed-text",
    "TimeoutSeconds": 300                // 0 = no timeout, required for long generations
  },
  "Ingestion": {
    "ChunkSize": 500,                    // words per chunk
    "ChunkOverlap": 50,                  // must be < ChunkSize, validated at startup
    "SupportedExtensions": [".pdf", ".docx", ".txt"],
    "TopChunkResults": 5,
    "MaxUploadBytes": 26214400           // 25 MiB
  },
  "Storage": {
    "UploadDirectory": "UploadedResumes",
    "AppDataRoot": null                  // null → resolved next to the assembly
  },
  "Screening": {
    "MinPromptLength": 1,
    "MaxPromptLength": 8000,
    "DefaultTopK": 5,
    "MaxTopK": 50
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"],
    "AllowCredentials": false            // validated: cannot be true with an origin list
  }
}
```

### Dual database provider

| | PostgreSQL | SQLite |
|---|---|---|
| Selected by | `"DatabaseProvider": "PostgreSQL"` | anything else (default) |
| Embedding column | `vector(768)` with the `vector` extension | `TEXT`, `float[]` serialised as JSON |
| Ranking | pgvector `<=>` operator, index-assisted | computed in memory in the application |
| Setup | PostgreSQL with `pgvector` installed | none |

The application layer never references pgvector. `ProviderAwareVectorChunkSearch` picks the branch
once and returns the same `ScoredChunk` shape either way, so switching providers changes no use case,
no endpoint, and no test.

PostgreSQL connection string:

```jsonc
{
  "DatabaseProvider": "PostgreSQL",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=portion_db;Username=postgres;Password=postgres"
  }
}
```

### Schema initialisation

`DatabaseInitializer` runs as a hosted service with bounded retry:

- **PostgreSQL** — `CREATE EXTENSION IF NOT EXISTS vector`, then `MigrateAsync()`.
- **SQLite, brand new or empty** — `MigrateAsync()`.
- **SQLite with a legacy hand-created schema** (the pre-refactor `portion_local.db`, which has tables
  but no `__EFMigrationsHistory`) — `EnsureCreatedAsync()`, so the existing local database keeps
  working instead of erroring on a half-migrated schema.

`IDesignTimeDbContextFactory` in Infrastructure means `dotnet ef` works against this project without
the API project being the startup project.

> To adopt real EF migrations, install the tool (`dotnet tool install --global dotnet-ef`) and run
> `dotnet ef migrations add Initial -s src/Portion.Api -p src/Portion.Infrastructure`. Then delete the
> legacy `portion_local.db` once, so it is created by the migration instead of by `EnsureCreated`.

### Frontend environment

| Variable | Default | Purpose |
|---|---|---|
| `VITE_API_BASE_URL` | `/api/v1` | API prefix. Relative by default so the dev proxy is used. |
| `VITE_POLL_INTERVAL_MS` | `5000` | Polling cadence, clamped to ≥ 500 ms. |

Vite proxies `/api` and `/health` to `http://localhost:5000` in both `dev` and `preview`, so the
browser never makes a cross-origin request and CORS is not exercised in normal development.

---

## 9. Getting started

### Prerequisites

- .NET SDK 10 (`dotnet --version` → `10.x`)
- Node.js 20+ and npm
- [Ollama](https://ollama.com) running locally
- PostgreSQL with `pgvector` — **optional**; SQLite is the zero-config default

```bash
ollama pull nomic-embed-text
ollama pull qwen2.5:1.5b
ollama serve
```

### Run everything

```powershell
.\start-all.ps1
```

Checks and starts PostgreSQL → Ollama → the API on `:5000` → the UI on `:5173`, skipping anything
already listening.

### Run manually

```bash
# Terminal 1 — API
dotnet run --project src/Portion.Api
# → http://localhost:5000   (Swagger UI at /swagger in Development)

# Terminal 2 — UI
cd portion-ui
npm install
npm run dev
# → http://localhost:5173
```

```bash
# Tests
dotnet test Portion.sln
cd portion-ui && npm run lint && npm run build
```

---

## 10. Testing

```
dotnet build Portion.sln    →  0 Warning(s), 0 Error(s)
dotnet test  Portion.sln    →  110 passed, 0 failed
npm run lint                →  0 warnings, 0 errors (48 files, 116 rules)
npm run build               →  tsc -b clean, vite build succeeded
```

| Project | Tests | Covers |
|---|---|---|
| `Portion.Domain.Tests` | 29 | `Result`/`Error` construction and implicit conversion, `ErrorType` mapping, `Guard` argument failures, `PagedResult` page arithmetic, `FileHash` normalisation |
| `Portion.Application.Tests` | 44 | `TextChunker` (size, overlap, tail, empty, whitespace-only, single-chunk), `VectorMath` (identical, orthogonal, opposite, mismatched dimensions, zero vector), `PromptBuilder` (grounding instructions present, context included), `ResumeSearchService` cascade (vector → keyword → first-available → none, LIKE metacharacter escaping, decayed scoring), all six feature handlers against a fake DbContext, `SqliteTestDatabase` round-trips |
| `Portion.Api.Tests` | 37 | `WebApplicationFactory` over a temp SQLite file: paged list shape and bounds validation, `404` ProblemDetails, upload happy path + `alreadyIndexed`, delete cascade, sync job lifecycle, SSE event order and terminal events, CORS preflight, `/health` and `/health/live`, hosting-level `404`/`405` ProblemDetails |

Integration tests run against a **temporary SQLite file**, never `portion_local.db`. The Ollama
dependency is replaced with test doubles registered through the `TryAdd` seams in
`DependencyInjection.cs`.

There is no committed frontend test suite — the UI was verified with a temporary headless-Chrome
harness outside the repo. See §12.

---

## 11. Design decisions worth knowing

**A `switch` on file extension became a strategy.** `IDocumentTextExtractor` + a factory means a new
format is one class and one registration, and each extractor is independently testable. The old
static `TextExtractor` could not be substituted at all.

**`Result<T>` for expected outcomes, exceptions for exceptional ones.** A resume that does not exist
is a `Result` failure, not a `KeyNotFoundException`. Exceptions are reserved for crossing layer
boundaries, and three `IExceptionHandler`s convert them to ProblemDetails. Internal `500` messages
are always generic; the real exception goes to the log with the trace id.

**A DI scope per ingestion item.** The worker is a long-lived singleton. Resolving a scoped
`DbContext` once would accumulate tracked entities for the process lifetime and cross-contaminate
change tracking between documents.

**Chunks are deleted before re-insert.** Re-ingesting an edited resume would otherwise append a
second copy of every chunk and silently double every search result. This was a real bug in the
original worker.

**`IClock` and `IHashCalculator` exist as interfaces.** Not over-abstraction: both are the only
sources of non-determinism in the pipeline, and both would otherwise force tolerance-based assertions
into every test.

**CORS uses an explicit origin list, and credentials are structurally forbidden.** A startup
validator fails the boot if `AllowCredentials` is ever set to `true` alongside an origin list, which
is an invalid combination that some stacks silently mis-handle.

**Rate limiting is per-endpoint.** Screening and upload have their own named policies; a global
limiter would let a long SSE stream starve ordinary reads.

**The frontend never fabricates a metric.** Every number rendered — match score, chunk count,
embedded count, elapsed time, sync statistics — comes from a real API response. When there is no
match data, the UI shows an empty state.

---

## 12. Known gaps and pending work

1. **`Portion.Server/` still contains the pre-refactor sources** (`Controllers/`, `Data/`, `Entities/`,
   `Infrastructure/`, `Middleware/`, `Models/`, `Services/`, `Utilities/`, `Workers/`, `Program.cs`).
   `Portion.Server.csproj` sets `EnableDefaultCompileItems=false` and compiles only
   `Host/PortionServerHost.cs`, so they are inert — but they are dead code and should be deleted once
   you are comfortable. Nothing depends on them. The compatibility host itself exists solely so the
   unmodified `start-all.ps1`, which launches `Portion.Server/Portion.Server.csproj`, keeps working;
   repointing that script at `src/Portion.Api` would let the whole folder go.
2. **`start-all.ps1` still launches the compatibility host.** Functionally identical
   (`PortionServerHost` delegates to `PortionApiHost`), but the path should be modernised.
3. **No EF Core migrations are generated.** `dotnet-ef` is not installed here and there is no
   migration baseline. The schema is additive-compatible, so `EnsureCreatedAsync()` on the legacy
   SQLite database is deliberate — see §8.
4. **No repository, therefore no git history.** There is no `.git` in this tree. `.gitignore` files
   exist only inside `portion-ui/`; a root one covering `bin/`, `obj/`, `*.db*`, and
   `UploadedResumes/` should be added before the first commit, along with a decision about whether
   `portion_local.db` and the uploaded CVs in `UploadedResumes/` belong in `.gitignore`. Those files
   contain real personal data and should not be committed.
5. **`scratch/sample_resume.txt`** is a stray development artefact at the repository root.
6. **No committed frontend tests.** A Vitest + Testing Library suite would lock in the SSE state
   machine and the polling behaviour. The dependency is not currently installed.
7. **`ISyncJobTracker` is in-memory.** Sync job history is lost on restart, and a multi-instance
   deployment would not see another instance's jobs. Persisting to the database is the fix.
8. **`ForwardedHeaders` clears the known-proxies list**, which is correct behind a trusted reverse
   proxy and wrong if the API is ever exposed directly. If that happens, populate
   `KnownIPNetworks`/`KnownProxies` instead.
9. **SQLite retrieval is `O(n)`** — the whole vector column is materialised in memory on every query.
   Fine for hundreds of resumes, not for tens of thousands. This is exactly what PostgreSQL + pgvector
   solves; the SQLite path exists so there is a zero-configuration mode at all.
10. **No authentication or authorisation.** Every endpoint is anonymous and the recruiter query is
    unauthenticated. The service is built for a trusted local network. Any exposure beyond localhost
    needs authn/authz, a real user story, and an audit trail over who asked what about which candidate.
