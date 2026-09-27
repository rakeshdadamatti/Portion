/**
 * Wire types mirroring the Portion.Server recruitment + screening API exactly.
 * Nothing in this file is invented: every field here is returned by a documented
 * endpoint. Any UI that needs a number must source it from one of these shapes.
 */

/* ── Shared envelope ──────────────────────────────────────────────────────── */

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** RFC 7807 `application/problem+json` body. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/* ── Resume registry ──────────────────────────────────────────────────────── */

export const RESUME_STATUSES = ['Discovered', 'Processing', 'Synced', 'Failed'] as const;
export type ResumeStatus = (typeof RESUME_STATUSES)[number];

export interface ResumeSummary {
  id: string;
  candidateName: string;
  fileName: string;
  filePath: string;
  status: ResumeStatus;
  failureReason: string | null;
  createdAt: string;
  lastSyncedAt: string;
  chunkCount: number;
  embeddedChunkCount: number;
}

export interface ResumeChunk {
  id: string;
  chunkIndex: number;
  characterCount: number;
  hasEmbedding: boolean;
  textContent: string;
}

export interface ResumeDetail extends ResumeSummary {
  chunks: ResumeChunk[];
}

export interface UploadResumeResponse {
  message: string;
  resumeId: string;
  alreadyIndexed: boolean;
}

export interface DeleteResumeOptions {
  deleteFile: boolean;
}

/* ── Folder sync ──────────────────────────────────────────────────────────── */

export const SYNC_JOB_STATES = ['Queued', 'Running', 'Completed', 'Failed'] as const;
export type SyncJobState = (typeof SYNC_JOB_STATES)[number];

export interface SyncRequest {
  folderPath: string;
}

export interface SyncJobAccepted {
  message: string;
  jobId: string;
}

export interface SyncJobStatus {
  jobId: string;
  folderPath: string;
  state: SyncJobState;
  discovered: number;
  skipped: number;
  queued: number;
  failed: number;
  startedAt: string | null;
  completedAt: string | null;
  error: string | null;
}

export function isSyncJobTerminal(state: SyncJobState): boolean {
  return state === 'Completed' || state === 'Failed';
}

/* ── Screening SSE ─────────────────────────────────────────────────────────── */

export const SCREENING_STAGES = ['embedding', 'search', 'fallback', 'generating', 'complete'] as const;
export type ScreeningStage = (typeof SCREENING_STAGES)[number];

export interface ScreeningStatusEvent {
  stage: ScreeningStage;
}

export interface ScreeningMatchCandidate {
  resumeId: string;
  candidateName: string;
  score: number;
  chunkCount: number;
}

export interface ScreeningMatchesEvent {
  topChunkCount: number;
  candidates: ScreeningMatchCandidate[];
}

export interface ScreeningDoneEvent {
  elapsedMs: number;
}

export interface ScreeningErrorEvent {
  message: string;
}

export interface ScreeningStreamQuery {
  prompt: string;
  topK: number;
}

/** Hard server-side limit: prompts longer than this are rejected with 400. */
export const SCREENING_MAX_PROMPT_LENGTH = 8000;
export const SCREENING_DEFAULT_TOP_K = 5;
