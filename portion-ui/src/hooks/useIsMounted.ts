import { useCallback, useEffect, useRef } from 'react';

/**
 * A ref that tracks whether the component is still mounted. Async callbacks
 * check it before touching state so an in-flight request can never warn about
 * updating an unmounted component.
 */
export function useIsMounted(): () => boolean {
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  return useCallback(() => mountedRef.current, []);
}
