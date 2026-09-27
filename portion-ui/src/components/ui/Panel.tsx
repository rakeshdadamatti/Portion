import type { ReactNode } from 'react';
import { cn } from '../../lib/cn';

export interface PanelProps {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  footer?: ReactNode;
  variant?: 'default' | 'narrow';
  className?: string;
  children: ReactNode;
}

/** Card container used by every page section. */
export function Panel({ title, description, actions, footer, variant = 'default', className, children }: PanelProps) {
  const hasHeader = title !== undefined || actions !== undefined;

  return (
    <section className={cn('panel', variant === 'narrow' && 'panel--narrow', className)}>
      {hasHeader ? (
        <header className="panel-header">
          <div className="panel-heading">
            {title !== undefined ? <h2 className="panel-title">{title}</h2> : null}
            {description !== undefined ? <p className="panel-desc">{description}</p> : null}
          </div>
          {actions !== undefined ? <div className="panel-actions">{actions}</div> : null}
        </header>
      ) : null}
      <div className="panel-body">{children}</div>
      {footer !== undefined ? <footer className="panel-footer">{footer}</footer> : null}
    </section>
  );
}
