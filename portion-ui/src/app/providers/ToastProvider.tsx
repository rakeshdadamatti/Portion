import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

export type ToastTone = 'success' | 'error' | 'info';

export interface Toast {
  id: string;
  tone: ToastTone;
  message: string;
}

export interface ToastContextValue {
  toasts: readonly Toast[];
  notify: (tone: ToastTone, message: string) => string;
  success: (message: string) => string;
  error: (message: string) => string;
  info: (message: string) => string;
  dismiss: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const DEFAULT_DURATION_MS = 5000;
const ERROR_DURATION_MS = 9000;
const MAX_VISIBLE_TOASTS = 4;

let toastCounter = 0;

function nextToastId(): string {
  toastCounter += 1;
  return `toast-${toastCounter}`;
}

export function ToastProvider({ children }: { children: ReactNode }): ReactNode {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const timersRef = useRef(new Map<string, number>());

  const dismiss = useCallback((id: string) => {
    const timer = timersRef.current.get(id);
    if (timer !== undefined) {
      window.clearTimeout(timer);
      timersRef.current.delete(id);
    }
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const notify = useCallback(
    (tone: ToastTone, message: string): string => {
      const id = nextToastId();
      const toast: Toast = { id, tone, message };
      setToasts((current) => [...current, toast].slice(-MAX_VISIBLE_TOASTS));
      const duration = tone === 'error' ? ERROR_DURATION_MS : DEFAULT_DURATION_MS;
      const timer = window.setTimeout(() => dismiss(id), duration);
      timersRef.current.set(id, timer);
      return id;
    },
    [dismiss],
  );

  // Clear every pending dismissal timer when the provider unmounts.
  useEffect(() => {
    const timers = timersRef.current;
    return () => {
      for (const timer of timers.values()) window.clearTimeout(timer);
      timers.clear();
    };
  }, []);

  const value = useMemo<ToastContextValue>(
    () => ({
      toasts,
      notify,
      success: (message: string) => notify('success', message),
      error: (message: string) => notify('error', message),
      info: (message: string) => notify('info', message),
      dismiss,
    }),
    [toasts, notify, dismiss],
  );

  return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>;
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (context === null) throw new Error('useToast must be used inside <ToastProvider>.');
  return context;
}
