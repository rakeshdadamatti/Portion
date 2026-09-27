import { forwardRef, useId, type InputHTMLAttributes, type ReactNode } from 'react';
import { cn } from '../../lib/cn';

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> {
  label: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
  /** Visually hide the label while keeping it for screen readers. */
  hideLabel?: boolean;
}

/** Labelled text input with hint + inline error slots wired to `aria-*`. */
export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, hint, error, hideLabel = false, className, id, ...rest },
  ref,
) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;

  return (
    <div className={cn('field', hideLabel && 'field--sr-label', error !== undefined && 'field--invalid', className)}>
      <label className="field__label" htmlFor={inputId}>
        {label}
      </label>
      <input
        ref={ref}
        id={inputId}
        className="field__input"
        aria-invalid={error !== undefined || undefined}
        aria-describedby={cn(hint !== undefined && hintId, error !== undefined && errorId) || undefined}
        {...rest}
      />
      {hint !== undefined ? (
        <p className="field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {error !== undefined ? (
        <p className="field__error" id={errorId} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
});
