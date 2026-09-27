const dateTimeFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
});

const dateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });

export const EMPTY_DATE_PLACEHOLDER = '—';

function toDate(value: string | null | undefined): Date | null {
  if (typeof value !== 'string' || value.length === 0) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Human readable local timestamp, e.g. "27 Sep 2026, 14:03". */
export function formatDateTime(value: string | null | undefined): string {
  const date = toDate(value);
  return date ? dateTimeFormatter.format(date) : EMPTY_DATE_PLACEHOLDER;
}

/** Human readable local date, e.g. "27 Sep 2026". */
export function formatDate(value: string | null | undefined): string {
  const date = toDate(value);
  return date ? dateFormatter.format(date) : EMPTY_DATE_PLACEHOLDER;
}

/** Machine readable value for a `<time dateTime>` attribute, or undefined. */
export function toMachineDate(value: string | null | undefined): string | undefined {
  return toDate(value)?.toISOString();
}

export function isValidDate(value: string | null | undefined): boolean {
  return toDate(value) !== null;
}
