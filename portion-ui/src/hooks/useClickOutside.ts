import { useEffect, useRef, type RefObject } from 'react';

/**
 * Calls `handler` on pointer/touch events that land outside `ref`, plus on
 * `Escape`. Used by every popover so menus close predictably.
 */
export function useClickOutside(
  ref: RefObject<HTMLElement | null>,
  handler: () => void,
  enabled = true,
): void {
  const handlerRef = useRef(handler);

  useEffect(() => {
    handlerRef.current = handler;
  }, [handler]);

  useEffect(() => {
    if (!enabled) return;

    const onPointerDown = (event: Event): void => {
      const element = ref.current;
      if (!element) return;
      if (event.target instanceof Node && element.contains(event.target)) return;
      handlerRef.current();
    };

    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') handlerRef.current();
    };

    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [ref, enabled]);
}
