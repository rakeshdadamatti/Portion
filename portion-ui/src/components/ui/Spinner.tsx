import { cn } from '../../lib/cn';

export interface SpinnerProps {
  size?: number;
  label?: string;
  className?: string;
}

export function Spinner({ size = 16, label, className }: SpinnerProps) {
  return (
    <span
      className={cn('spinner', className)}
      style={{ width: size, height: size }}
      role="status"
      aria-label={label ?? 'Loading'}
    />
  );
}
