import { Component, type ErrorInfo, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
  fallbackTitle?: string;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Catches render-time crashes anywhere below it and offers a recoverable
 * fallback: the component tree is unmounted and re-mounted by bumping the key,
 * so a transient failure never leaves a blank screen.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('[portion-ui] Unhandled render error', error, info.componentStack);
  }

  private readonly handleReset = (): void => {
    this.setState({ error: null });
  };

  private readonly handleReload = (): void => {
    window.location.reload();
  };

  override render(): ReactNode {
    const { error } = this.state;
    const { children, fallbackTitle = 'Something in the interface broke' } = this.props;

    if (error === null) return children;

    return (
      <div className="error-boundary" role="alert">
        <h1 className="error-boundary__title">{fallbackTitle}</h1>
        <p className="error-boundary__desc">
          The rest of the application is still fine. You can retry rendering this view, or reload the whole app.
        </p>
        <pre className="error-boundary__detail">{error.message}</pre>
        <div className="error-boundary__actions">
          <button type="button" className="btn btn--primary btn--md" onClick={this.handleReset}>
            <span className="btn__label">Try again</span>
          </button>
          <button type="button" className="btn btn--subtle btn--md" onClick={this.handleReload}>
            <span className="btn__label">Reload app</span>
          </button>
        </div>
      </div>
    );
  }
}
