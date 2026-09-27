import { env } from '../config/env';
import type { ProblemDetails } from '../types/api';

/* ─── Errors ───────────────────────────────────────────────────────────────── */

export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'aborted';

export interface ApiErrorInit {
  kind: ApiErrorKind;
  status: number;
  url: string;
  problem?: ProblemDetails | null;
  cause?: unknown;
}

/**
 * The single error type every network layer throws. Carries the parsed
 * RFC 7807 ProblemDetails (when the server sent one) plus a human readable
 * message, so callers never have to string-match on `fetch` responses and no
 * error is ever thrown away.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind;
  readonly status: number;
  readonly url: string;
  readonly problem: ProblemDetails | null;

  constructor(message: string, init: ApiErrorInit) {
    super(message, init.cause === undefined ? undefined : { cause: init.cause });
    this.name = 'ApiError';
    this.kind = init.kind;
    this.status = init.status;
    this.url = init.url;
    this.problem = init.problem ?? null;
  }

  get isAborted(): boolean {
    return this.kind === 'aborted';
  }

  get isTimeout(): boolean {
    return this.kind === 'timeout';
  }

  /** Per-field validation messages from ProblemDetails `errors`. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {};
  }

  get traceId(): string | null {
    return this.problem?.traceId ?? null;
  }
}

/** Normalises anything thrown into a message safe to show a user. */
export function toUserMessage(error: unknown, fallback = 'Something went wrong.'): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error && error.message.length > 0) return error.message;
  return fallback;
}

export function isAbortError(error: unknown): boolean {
  return error instanceof ApiError ? error.isAborted : error instanceof Error && error.name === 'AbortError';
}

/* ─── Request plumbing ────────────────────────────────────────────────────── */

export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue>;

export interface HttpRequestOptions {
  method?: HttpMethod;
  query?: QueryParams;
  body?: BodyInit | null;
  headers?: Record<string, string>;
  signal?: AbortSignal | null;
  timeoutMs?: number;
  accept?: string;
}

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

const DEFAULT_TIMEOUT_MS = 30_000;

/** Joins the API base URL, a path and a query string into a request URL. */
export function buildUrl(path: string, query?: QueryParams): string {
  const isAbsolute = /^https?:\/\//i.test(path);
  const base = isAbsolute ? '' : env.apiBaseUrl;
  const suffix = path.startsWith('/') ? path : `/${path}`;
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === null || value === undefined || value === '') continue;
    search.append(key, String(value));
  }
  const qs = search.toString();
  return `${base}${suffix}${qs ? `?${qs}` : ''}`;
}

function linkSignals(sources: readonly (AbortSignal | null | undefined)[]): {
  signal: AbortSignal;
  dispose: () => void;
} {
  const controller = new AbortController();
  const detach: Array<() => void> = [];

  for (const source of sources) {
    if (!source) continue;
    if (source.aborted) {
      controller.abort(source.reason);
      break;
    }
    const onAbort = (): void => controller.abort(source.reason);
    source.addEventListener('abort', onAbort, { once: true });
    detach.push(() => source.removeEventListener('abort', onAbort));
  }

  return {
    signal: controller.signal,
    dispose: () => {
      for (const off of detach) off();
    },
  };
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return typeof record.title === 'string' || typeof record.status === 'number';
}

async function readProblemDetails(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.toLowerCase().includes('json')) {
    await response.text().catch(() => undefined);
    return null;
  }
  try {
    const parsed: unknown = await response.json();
    return isProblemDetails(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function httpErrorMessage(response: Response, problem: ProblemDetails | null): string {
  const detail = problem?.detail?.trim();
  if (detail) return detail;
  const title = problem?.title?.trim();
  if (title) return `${title} (HTTP ${response.status})`;
  if (response.status === 404) return 'Not found (HTTP 404).';
  if (response.status === 413) return 'That file is too large to process (HTTP 413).';
  if (response.status >= 500) return `The server failed to handle the request (HTTP ${response.status}).`;
  return `Request failed with HTTP ${response.status}.`;
}

async function send(path: string, options: HttpRequestOptions): Promise<Response> {
  const { method = 'GET', query, body, headers, signal, timeoutMs = DEFAULT_TIMEOUT_MS, accept } = options;
  const url = buildUrl(path, query);

  const timeoutController = new AbortController();
  let timedOut = false;
  const timer =
    timeoutMs > 0
      ? setTimeout(() => {
          timedOut = true;
          timeoutController.abort();
        }, timeoutMs)
      : null;

  const linked = linkSignals([signal, timeoutController.signal]);

  const requestHeaders: Record<string, string> = { Accept: accept ?? 'application/json, text/plain;q=0.9, */*;q=0.8' };
  if (body !== null && body !== undefined && !(body instanceof FormData)) {
    requestHeaders['Content-Type'] = 'application/json';
  }
  Object.assign(requestHeaders, headers);

  try {
    const response = await fetch(url, {
      method,
      headers: requestHeaders,
      body: body ?? null,
      signal: linked.signal,
      credentials: 'same-origin',
    });

    if (!response.ok) {
      const problem = await readProblemDetails(response);
      throw new ApiError(httpErrorMessage(response, problem), { kind: 'http', status: response.status, url, problem });
    }

    return response;
  } catch (cause) {
    if (cause instanceof ApiError) throw cause;
    if (linked.signal.aborted) {
      throw timedOut
        ? new ApiError(`The request timed out after ${timeoutMs}ms.`, { kind: 'timeout', status: 0, url, cause })
        : new ApiError('The request was cancelled.', { kind: 'aborted', status: 0, url, cause });
    }
    throw new ApiError('Could not reach the Portion API. Is the server running?', {
      kind: 'network',
      status: 0,
      url,
      cause,
    });
  } finally {
    if (timer !== null) clearTimeout(timer);
    linked.dispose();
  }
}

async function parseBody<T>(response: Response, url: string): Promise<T> {
  if (response.status === 204) return undefined as T;
  const raw = await response.text();
  if (raw.length === 0) return undefined as T;
  try {
    return JSON.parse(raw) as T;
  } catch (cause) {
    throw new ApiError('The API returned a response that is not valid JSON.', {
      kind: 'http',
      status: response.status,
      url,
      cause,
    });
  }
}

/** Performs a request and parses a JSON (or text) body. */
export async function request<T>(path: string, options: HttpRequestOptions = {}): Promise<T> {
  const response = await send(path, options);
  return parseBody<T>(response, buildUrl(path, options.query));
}

/** Performs a request and discards the body (`204 No Content`). */
export async function requestVoid(path: string, options: HttpRequestOptions = {}): Promise<void> {
  const response = await send(path, options);
  await response.text().catch(() => undefined);
}

export function postJson<T>(path: string, body: unknown, options: Omit<HttpRequestOptions, 'body' | 'method'> = {}): Promise<T> {
  return request<T>(path, { ...options, method: 'POST', body: JSON.stringify(body) });
}
