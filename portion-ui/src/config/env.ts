const DEFAULT_API_BASE_URL = '/api/v1';
const DEFAULT_POLL_INTERVAL_MS = 5000;
const MIN_POLL_INTERVAL_MS = 500;

/**
 * Typed, validated access to the Vite environment.
 * `ImportMetaEnv` is augmented here (instead of a separate d.ts) so the env
 * contract lives next to the code that reads it.
 */
declare global {
  interface ImportMetaEnv {
    readonly VITE_API_BASE_URL?: string;
    readonly VITE_POLL_INTERVAL_MS?: string;
  }
}

function readText(raw: unknown, fallback: string): string {
  return typeof raw === 'string' && raw.trim().length > 0 ? raw.trim() : fallback;
}

function readPositiveInteger(raw: unknown, fallback: number, min: number): number {
  const parsed = Number.parseInt(readText(raw, ''), 10);
  return Number.isFinite(parsed) && parsed >= min ? parsed : fallback;
}

/** Strips trailing slashes so `${apiBaseUrl}/path` never double-slashes. */
function normaliseBaseUrl(raw: unknown): string {
  const value = readText(raw, DEFAULT_API_BASE_URL);
  const trimmed = value.replace(/\/+$/, '');
  return trimmed.length > 0 ? trimmed : DEFAULT_API_BASE_URL;
}

export const env = {
  /** Base URL every REST call is prefixed with, e.g. `/api/v1`. */
  apiBaseUrl: normaliseBaseUrl(import.meta.env.VITE_API_BASE_URL),
  /** Default cadence (ms) for `usePolling` driven auto-refresh. */
  pollIntervalMs: readPositiveInteger(import.meta.env.VITE_POLL_INTERVAL_MS, DEFAULT_POLL_INTERVAL_MS, MIN_POLL_INTERVAL_MS),
} as const;

export const ENV_KEYS = ['VITE_API_BASE_URL', 'VITE_POLL_INTERVAL_MS'] as const;
