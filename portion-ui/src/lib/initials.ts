const FALLBACK_INITIAL = '?';

/**
 * Derives an avatar label from a real candidate name. No hashing, no faker —
 * just the first letters of the first and last whitespace-separated words.
 */
export function initials(name: string | null | undefined, max = 2): string {
  if (typeof name !== 'string') return FALLBACK_INITIAL;
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return FALLBACK_INITIAL;
  if (words.length === 1) {
    const [word] = words;
    return word.slice(0, max).toUpperCase();
  }
  return words
    .slice(0, max)
    .map((word) => word.charAt(0))
    .join('')
    .toUpperCase();
}
