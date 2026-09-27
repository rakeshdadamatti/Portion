import { useCallback, useEffect, useRef, useState } from 'react';
import { useIsMounted } from './useIsMounted';
import { isAbortError } from '../services/httpClient';

export interface UsePollingOptions {
  intervalMs: number;
  /** Polling only runs while this is true. */
  enabled: boolean;
  /** Fire once immediately when polling becomes enabled. */
  immediate?: boolean;
  onError?: (error: Error) => void;
}

export interface UsePollingResult {
  /** Runs the callback now, honouring the in-flight guard. */
  refresh: () => Promise<void>;
  /** True while the interval is armed (i.e. `enabled` is true). */
  isPolling: boolean;
  isRefreshing: boolean;
}

/**
 * Interval polling that never overlaps itself, pauses ticks while the tab is
 * hidden, and cleans its timer up on unmount. Failures are surfaced through
 * `onError` rather than being swallowed.
 */
export function usePolling(callback: () => void | Promise<void>, options: UsePollingOptions): UsePollingResult {
  const { intervalMs, enabled, immediate = true, onError } = options;

  const callbackRef = useRef(callback);
  const onErrorRef = useRef(onError);
  useEffect(() => {
    callbackRef.current = callback;
    onErrorRef.current = onError;
  }, [callback, onError]);

  const isMounted = useIsMounted();
  const inFlightRef = useRef(false);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const refresh = useCallback(async (): Promise<void> => {
    if (inFlightRef.current || !isMounted()) return;
    inFlightRef.current = true;
    setIsRefreshing(true);
    try {
      await callbackRef.current();
    } catch (caught) {
      if (isAbortError(caught)) return;
      const normalised = caught instanceof Error ? caught : new Error(String(caught));
      onErrorRef.current?.(normalised);
    } finally {
      inFlightRef.current = false;
      if (isMounted()) setIsRefreshing(false);
    }
  }, [isMounted]);

  useEffect(() => {
    if (!enabled) return;

    if (immediate) void refresh();

    const timer = window.setInterval(() => {
      if (document.hidden) return;
      void refresh();
    }, Math.max(intervalMs, 500));

    return () => window.clearInterval(timer);
  }, [enabled, intervalMs, immediate, refresh]);

  return { refresh, isPolling: enabled, isRefreshing };
}
