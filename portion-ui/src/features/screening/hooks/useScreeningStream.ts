import { useCallback, useEffect, useRef, useState } from 'react';
import { openScreeningStream, validatePrompt, type ScreeningStream } from '../../../services/screeningApi';
import { useToast } from '../../../app/providers/ToastProvider';
import {
  SCREENING_DEFAULT_TOP_K,
  type ScreeningMatchesEvent,
  type ScreeningStage,
} from '../../../types/api';

export type ChatRole = 'user' | 'assistant';

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text: string;
  /** True until the first `token` arrives; drives the thinking indicator. */
  pending: boolean;
}

export interface UseScreeningStreamResult {
  messages: readonly ChatMessage[];
  isStreaming: boolean;
  stage: ScreeningStage | null;
  matches: ScreeningMatchesEvent | null;
  elapsedMs: number | null;
  send: (prompt: string) => void;
  cancel: () => void;
  reset: () => void;
}

let messageCounter = 0;

function nextMessageId(prefix: string): string {
  messageCounter += 1;
  return `${prefix}-${messageCounter}`;
}

/**
 * Owns the single `EventSource` for the screening page and maps the five named
 * SSE events onto React state:
 *
 *  - `status`  -> `stage` (drives the pipeline status line)
 *  - `matches` -> `matches` (drives the match panel + bar chart)
 *  - `token`   -> appended onto the in-flight assistant message
 *  - `done`    -> `elapsedMs`, stream closed, `isStreaming` cleared
 *  - `error`   -> toast + inline error, stream closed, `isStreaming` cleared
 *
 * The stream is closed on `done`, on `error`, when a new prompt supersedes an
 * in-flight one, and on unmount.
 */
export function useScreeningStream(topK: number = SCREENING_DEFAULT_TOP_K): UseScreeningStreamResult {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [stage, setStage] = useState<ScreeningStage | null>(null);
  const [matches, setMatches] = useState<ScreeningMatchesEvent | null>(null);
  const [elapsedMs, setElapsedMs] = useState<number | null>(null);

  const streamRef = useRef<ScreeningStream | null>(null);
  const activeMessageIdRef = useRef<string | null>(null);
  const isMountedRef = useRef(true);
  const toast = useToast();

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      streamRef.current?.close();
      streamRef.current = null;
      activeMessageIdRef.current = null;
    };
  }, []);

  const closeStream = useCallback((): void => {
    streamRef.current?.close();
    streamRef.current = null;
    activeMessageIdRef.current = null;
    if (isMountedRef.current) setIsStreaming(false);
  }, []);

  const appendToActiveMessage = useCallback((append: (current: string) => string): void => {
    const messageId = activeMessageIdRef.current;
    if (messageId === null) return;
    setMessages((current) =>
      current.map((message) =>
        message.id === messageId && message.role === 'assistant'
          ? { ...message, text: append(message.text), pending: false }
          : message,
      ),
    );
  }, []);

  const send = useCallback(
    (rawPrompt: string): void => {
      const prompt = rawPrompt.trim();
      const invalid = validatePrompt(prompt);
      if (invalid !== null) {
        toast.error(invalid);
        return;
      }
      if (streamRef.current !== null) {
        // A new prompt supersedes whatever is in flight.
        closeStream();
      }

      const assistantId = nextMessageId('assistant');
      activeMessageIdRef.current = assistantId;
      setMessages((current) => [
        ...current,
        { id: nextMessageId('user'), role: 'user', text: prompt, pending: false },
        { id: assistantId, role: 'assistant', text: '', pending: true },
      ]);
      setStage(null);
      setMatches(null);
      setElapsedMs(null);
      setIsStreaming(true);

      const stream = openScreeningStream(
        prompt,
        {
          onStatus: (event) => {
            if (isMountedRef.current) setStage(event.stage);
          },
          onMatches: (event) => {
            if (isMountedRef.current) setMatches(event);
          },
          onToken: (token) => appendToActiveMessage((text) => text + token),
          onDone: (event) => {
            if (isMountedRef.current) {
              setElapsedMs(event.elapsedMs);
              setIsStreaming(false);
              setStage('complete');
            }
            stream.close();
            if (streamRef.current === stream) streamRef.current = null;
            activeMessageIdRef.current = null;
          },
          onError: (event) => {
            toast.error(event.message);
            if (isMountedRef.current) {
              setIsStreaming(false);
              setMessages((current) =>
                current.map((message) =>
                  message.id === assistantId && message.text.length === 0
                    ? { ...message, text: `_${event.message}_`, pending: false }
                    : message.pending
                      ? { ...message, pending: false }
                      : message,
                ),
              );
            }
            stream.close();
            if (streamRef.current === stream) streamRef.current = null;
            activeMessageIdRef.current = null;
          },
        },
        { topK },
      );

      streamRef.current = stream;
    },
    [appendToActiveMessage, closeStream, toast, topK],
  );

  const cancel = useCallback((): void => {
    closeStream();
  }, [closeStream]);

  const reset = useCallback((): void => {
    closeStream();
    setMessages([]);
    setStage(null);
    setMatches(null);
    setElapsedMs(null);
  }, [closeStream]);

  return { messages, isStreaming, stage, matches, elapsedMs, send, cancel, reset };
}
