import type { CSSProperties } from 'react';
import { cn } from '../../lib/cn';
import type { ResumeStatus, SyncJobState } from '../../types/api';

type BadgeStatus = ResumeStatus | SyncJobState;

/** Exact status -> colour mapping preserved from the original design. */
const STATUS_COLORS: Record<BadgeStatus, string> = {
  Synced: '#22c55e',
  Processing: '#f59e0b',
  Discovered: '#60a5fa',
  Failed: '#ef4444',
  Queued: '#60a5fa',
  Running: '#f59e0b',
  Completed: '#22c55e',
};

export interface StatusBadgeProps {
  /** Pass `''` when the server has no row to describe — never a guessed status. */
  status: BadgeStatus | '';
  unknownLabel?: string;
  className?: string;
}

export function StatusBadge({ status, unknownLabel, className }: StatusBadgeProps) {
  if (status === '') {
    return (
      <span className={cn('status-badge', 'status-badge--unknown', className)}>
        {unknownLabel ?? 'Not indexed'}
      </span>
    );
  }

  const color = STATUS_COLORS[status];
  return (
    <span className={cn('status-badge', className)} style={{ '--badge-color': color } as CSSProperties}>
      {status}
    </span>
  );
}
