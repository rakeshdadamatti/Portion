import { useCallback, useEffect, useRef, useState } from 'react';
import { useIsMounted } from './useIsMounted';
import { isAbortError } from '../services/httpClient';

export interface UseAsyncResult<TArgs extends unknown[], T> {
  data: T | null;
  error: Error | null;
  isLoading: boolean;
  run: (...args: TArgs) => Promise<T | null>;
  setData: (updater: T | null | ((current: T | null) => T | null)) => void;
  reset: () => void;
}

/**
 * Runs an async function while exposing data / error / loading state.
 *
 * The callback is held in a ref (no dependency array at the call site) and
 * every run gets a sequence number, so a slow earlier response can never
 * overwrite a newer one. Failures always land in `error` — nothing is caught
 * and discarded.
 */
export function useAsync<TArgs extends unknown[], T>(
  fn: (...args: TArgs) => Promise<T>,
  options: { onError?: (error: Error) => void } = {},
): UseAsyncResult<TArgs, T> {
  const fnRef = useRef(fn);
  const onErrorRef = useRef(options.onError);
  useEffect(() => {
    fnRef.current = fn;
    onErrorRef.current = options.onError;
  }, [fn, options]);

  const isMounted = useIsMounted();
  const sequenceRef = useRef(0);

  const [data, setDataState] = useState<T | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const setData = useCallback((updater: T | null | ((current: T | null) => T | null)) => {
    if (!isMounted()) return;
    setDataState((current) => (typeof updater === 'function' ? (updater as (c: T | null) => T | null)(current) : updater));
  }, [isMounted]);

  const reset = useCallback(() => {
    sequenceRef.current += 1;
    if (!isMounted()) return;
    setDataState(null);
    setError(null);
    setIsLoading(false);
  }, [isMounted]);

  const run = useCallback(
    async (...args: TArgs): Promise<T | null> => {
      const sequence = ++sequenceRef.current;
      if (isMounted()) {
        setIsLoading(true);
        setError(null);
      }

      try {
        const result = await fnRef.current(...args);
        if (!isMounted() || sequence !== sequenceRef.current) return null;
        setDataState(result);
        return result;
      } catch (caught) {
        if (!isMounted() || sequence !== sequenceRef.current) return null;
        if (isAbortError(caught)) return null;
        const normalised = caught instanceof Error ? caught : new Error(String(caught));
        setError(normalised);
        onErrorRef.current?.(normalised);
        return null;
      } finally {
        if (isMounted() && sequence === sequenceRef.current) setIsLoading(false);
      }
    },
    [isMounted],
  );

  return { data, error, isLoading, run, setData, reset };
}
