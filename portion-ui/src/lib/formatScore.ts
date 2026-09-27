/**
 * Cosine similarity is reported by the API in 0..1. Every score in the UI is
 * rendered as a percentage of that real value — never rescaled or invented.
 */
export function formatScore(score: number): string {
  if (!Number.isFinite(score)) return '—';
  return `${(score * 100).toFixed(1)}%`;
}

/** A cosine score as a 0-100 number, for chart domains. */
export function scoreToPercent(score: number): number {
  return Number.isFinite(score) ? score * 100 : 0;
}
