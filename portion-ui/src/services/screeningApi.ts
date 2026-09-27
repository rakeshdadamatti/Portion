import { buildUrl, ApiError } from './httpClient';
import {
  SCREENING_DEFAULT_TOP_K,
  SCREENING_MAX_PROMPT_LENGTH,
  SCREENING_STAGES,
  type ScreeningDoneEvent,
  type ScreeningErrorEvent,
  type ScreeningMatchesEvent,
  type ScreeningStage,
  type ScreeningStatusEvent,
} from '../types/api';

const STREAM_PATH = '/screening/stream';

export interface ScreeningStreamHandlers {
  onStatus: (event: ScreeningStatusEvent) => void;
  onMatches: (event: ScreeningMatchesEvent) => void;
  onToken: (token: string) => void;
  onDone: (event: ScreeningDoneEvent) => void;
  onError: (event: ScreeningErrorEvent) => void;
}

export interface ScreeningStream {
  close: () => void;
  isClosed: () => boolean;
}

export interface OpenScreeningStreamOptions {
  topK?: number;
}

/**
 * The backend rejects a blank prompt or one longer than
 * `SCREENING_MAX_PROMPT_LENGTH` with a 400 ProblemDetails. `EventSource` cannot
 * read a response body, so the guard is mirrored client side to fail fast with
 * a real message instead of an opaque transport error.
 */
export function validatePrompt(prompt: string): string | null {
  const trimmed = prompt.trim();
  if (trimmed.length === 0) return 'Enter a screening prompt before sending.';
  if (trimmed.length > SCREENING_MAX_PROMPT_LENGTH) {
    return `Prompt is ${trimmed.length} characters — the limit is ${SCREENING_MAX_PROMPT_LENGTH}.`;
  }
  return null;
}

/** `GET /api/v1/screening/stream?prompt=…&topK=…` */
export function buildScreeningStreamUrl(prompt: string, topK = SCREENING_DEFAULT_TOP_K): string {
  return buildUrl(STREAM_PATH, { prompt: prompt.trim(), topK });
}

function messageData(event: Event): string {
  return (event as MessageEvent<string>).data ?? '';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseJsonObject(raw: string): Record<string, unknown> | null {
  try {
    const parsed: unknown = JSON.parse(raw);
    return isRecord(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function parseStatusEvent(raw: string): ScreeningStatusEvent | null {
  const parsed = parseJsonObject(raw);
  const stage = parsed?.['stage'];
  if (typeof stage !== 'string' || !(SCREENING_STAGES as readonly string[]).includes(stage)) return null;
  return { stage: stage as ScreeningStage };
}

function toFiniteNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function parseMatchesEvent(raw: string): ScreeningMatchesEvent | null {
  const parsed = parseJsonObject(raw);
  const topChunkCount = toFiniteNumber(parsed?.['topChunkCount']);
  const candidates = parsed?.['candidates'];
  if (topChunkCount === null || !Array.isArray(candidates)) return null;

  const parsedCandidates = candidates.flatMap((entry) => {
    if (!isRecord(entry)) return [];
    const resumeId = entry['resumeId'];
    const candidateName = entry['candidateName'];
    const score = toFiniteNumber(entry['score']);
    const chunkCount = toFiniteNumber(entry['chunkCount']);
    if (typeof resumeId !== 'string' || typeof candidateName !== 'string' || score === null) return [];
    return [{ resumeId, candidateName, score, chunkCount: chunkCount ?? 0 }];
  });

  return { topChunkCount, candidates: parsedCandidates };
}

function parseDoneEvent(raw: string): ScreeningDoneEvent {
  const parsed = parseJsonObject(raw);
  return { elapsedMs: toFiniteNumber(parsed?.['elapsedMs']) ?? 0 };
}

function parseErrorEvent(raw: string): ScreeningErrorEvent {
  const parsed = parseJsonObject(raw);
  const message = parsed?.['message'];
  return { message: typeof message === 'string' && message.length > 0 ? message : 'The screening stream reported an error.' };
}

/**
 * Opens the screening SSE stream and wires the five named events onto typed
 * handlers. The returned handle must be `close()`d by the caller on `done`,
 * `error`, prompt supersession and unmount.
 */
export function openScreeningStream(
  prompt: string,
  handlers: ScreeningStreamHandlers,
  options: OpenScreeningStreamOptions = {},
): ScreeningStream {
  const invalid = validatePrompt(prompt);
  if (invalid !== null) {
    handlers.onError({ message: invalid });
    return { close: () => undefined, isClosed: () => true };
  }

  const url = buildScreeningStreamUrl(prompt, options.topK ?? SCREENING_DEFAULT_TOP_K);
  const source = new EventSource(url);
  let closed = false;
  let receivedEvent = false;

  const close = (): void => {
    if (closed) return;
    closed = true;
    source.close();
  };

  source.addEventListener('status', (event) => {
    receivedEvent = true;
    const parsed = parseStatusEvent(messageData(event));
    if (parsed !== null) handlers.onStatus(parsed);
  });

  source.addEventListener('matches', (event) => {
    receivedEvent = true;
    const parsed = parseMatchesEvent(messageData(event));
    if (parsed !== null) handlers.onMatches(parsed);
  });

  source.addEventListener('token', (event) => {
    receivedEvent = true;
    const raw = messageData(event);
    if (raw.length === 0) return;
    // `token` payloads are a JSON-escaped single-line string; fall back to the
    // raw payload if the server ever emits an unquoted one.
    let token: string;
    try {
      const decoded: unknown = JSON.parse(raw);
      token = typeof decoded === 'string' ? decoded : raw;
    } catch {
      token = raw;
    }
    handlers.onToken(token.replace(/\\n/g, '\n'));
  });

  source.addEventListener('done', (event) => {
    receivedEvent = true;
    handlers.onDone(parseDoneEvent(messageData(event)));
    close();
  });

  source.addEventListener('error', (event) => {
    receivedEvent = true;
    // A named `error` event carries a ProblemDetails-ish body; a bare transport
    // failure arrives without one. `EventSource` does not expose the status.
    const raw = messageData(event);
    const parsed = raw.length > 0 ? parseErrorEvent(raw) : null;
    handlers.onError(
      parsed ?? {
        message: receivedEvent
          ? 'The screening stream was interrupted before it finished.'
          : 'The screening stream could not be started — the prompt may have been rejected or the API is unreachable.',
      },
    );
    close();
  });

  return { close, isClosed: () => closed };
}

export function screeningStreamError(message: string, cause?: unknown): ApiError {
  return new ApiError(message, { kind: 'network', status: 0, url: STREAM_PATH, cause });
}
