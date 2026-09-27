import { Outlet } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Info, X } from 'lucide-react';
import { AppHeader } from '../components/layout/AppHeader';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { useToast } from './providers/ToastProvider';
import { cn } from '../lib/cn';
import type { Toast, ToastTone } from './providers/ToastProvider';

const TONE_ICONS: Record<ToastTone, typeof Info> = {
  info: Info,
  success: CheckCircle2,
  error: AlertTriangle,
};

function ToastHost() {
  const { toasts, dismiss } = useToast();

  return (
    <div className="toast-host" role="region" aria-label="Notifications">
      {toasts.map((toast: Toast) => {
        const Icon = TONE_ICONS[toast.tone];
        return (
          <div key={toast.id} className={cn('status-toast', `status-toast--${toast.tone}`)} role="status">
            <Icon size={15} className="toast-icon" aria-hidden="true" />
            <span className="status-toast__message">{toast.message}</span>
            <button type="button" className="status-toast__close" onClick={() => dismiss(toast.id)} aria-label="Dismiss notification">
              <X size={13} aria-hidden="true" />
            </button>
          </div>
        );
      })}
    </div>
  );
}

/** Application shell: header + nav + routed content + toast host. */
export function App() {
  return (
    <div className="app-shell">
      <AppHeader />
      <main className="app-main">
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </main>
      <ToastHost />
    </div>
  );
}
