import { useCallback, useEffect, useRef, useState } from 'react';
import { useToast } from '../../../app/providers/ToastProvider';
import { isAbortError, toUserMessage } from '../../../services/httpClient';
import { isSupportedResumeFile, uploadResume } from '../../../services/resumeApi';
import type { UploadResumeResponse } from '../../../types/api';

export type UploadPhase = 'idle' | 'uploading' | 'done' | 'failed';

export interface UseResumeUploadResult {
  phase: UploadPhase;
  progressLabel: string | null;
  result: UploadResumeResponse | null;
  /** Metadata of the file the user picked, for display only. */
  lastFile: { name: string; size: number } | null;
  error: string | null;
  upload: (file: File) => Promise<UploadResumeResponse | null>;
  reset: () => void;
  /** Increments on every accepted upload so other pages can refetch. */
  completedCount: number;
}

/**
 * Wraps the multipart upload. `fetch` cannot report upload progress, so the
 * pending state is the single "Uploading…" phase and the server's `message`
 * (including the `alreadyIndexed` case) is surfaced verbatim.
 */
export function useResumeUpload(): UseResumeUploadResult {
  const toast = useToast();
  const [phase, setPhase] = useState<UploadPhase>('idle');
  const [progressLabel, setProgressLabel] = useState<string | null>(null);
  const [result, setResult] = useState<UploadResumeResponse | null>(null);
  const [lastFile, setLastFile] = useState<{ name: string; size: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [completedCount, setCompletedCount] = useState(0);

  const controllerRef = useRef<AbortController | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      controllerRef.current?.abort();
      controllerRef.current = null;
    };
  }, []);

  const reset = useCallback(() => {
    setPhase('idle');
    setProgressLabel(null);
    setResult(null);
    setLastFile(null);
    setError(null);
  }, []);

  const upload = useCallback(
    async (file: File): Promise<UploadResumeResponse | null> => {
      if (!isSupportedResumeFile(file)) {
        const message = `“${file.name}” is not supported. Upload a .pdf, .docx or .txt file.`;
        setPhase('failed');
        setError(message);
        toast.error(message);
        return null;
      }

      controllerRef.current?.abort();
      const controller = new AbortController();
      controllerRef.current = controller;

      setPhase('uploading');
      setError(null);
      setResult(null);
      setLastFile({ name: file.name, size: file.size });
      setProgressLabel(`Uploading “${file.name}”…`);

      try {
        const response = await uploadResume(file, { signal: controller.signal });
        if (!isMountedRef.current) return null;
        setPhase('done');
        setResult(response);
        setProgressLabel(null);
        setCompletedCount((count) => count + 1);
        if (response.alreadyIndexed) {
          toast.info(response.message);
        } else {
          toast.success(response.message);
        }
        return response;
      } catch (caught) {
        if (isAbortError(caught) || !isMountedRef.current) return null;
        const message = toUserMessage(caught, 'The upload failed.');
        setPhase('failed');
        setError(message);
        setProgressLabel(null);
        toast.error(message);
        return null;
      } finally {
        if (controllerRef.current === controller) controllerRef.current = null;
      }
    },
    [toast],
  );

  return { phase, progressLabel, result, lastFile, error, upload, reset, completedCount };
}
