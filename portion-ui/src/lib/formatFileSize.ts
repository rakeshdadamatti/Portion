const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;
const STEP = 1024;

/** Binary-ish human readable byte count, e.g. `1.4 MB`. */
export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < STEP) return `${bytes} B`;

  let value = bytes;
  let unitIndex = 0;
  while (value >= STEP && unitIndex < UNITS.length - 1) {
    value /= STEP;
    unitIndex += 1;
  }
  const decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
  return `${value.toFixed(decimals)} ${UNITS[unitIndex]}`;
}

/** `file.pdf` -> `PDF`, `archive.tar.gz` -> `GZ`. */
export function fileExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  if (dotIndex < 0 || dotIndex === fileName.length - 1) return '';
  return fileName.slice(dotIndex + 1).toLowerCase();
}
