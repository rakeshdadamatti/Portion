import type { ReactNode } from 'react';
import { cn } from '../../lib/cn';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}

/** Explicit "there is no data" state — never a substitute for fabricated values. */
export function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
  return (
    <div className={cn('empty-state', className)} role="status">
      {icon !== undefined ? <div className="empty-state__icon">{icon}</div> : null}
      <p className="empty-state__title">{title}</p>
      {description !== undefined ? <p className="empty-state__desc">{description}</p> : null}
      {action !== undefined ? <div className="empty-state__action">{action}</div> : null}
    </div>
  );
}
