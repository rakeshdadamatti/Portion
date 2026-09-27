import { useCallback, useEffect, useRef, useState } from 'react';
import { useToast } from '../../../app/providers/ToastProvider';
import { env } from '../../../config/env';
import { isAbortError, toUserMessage } from '../../../services/httpClient';
import { getSyncJob, startSync } from '../../../services/syncApi';
import { isSyncJobTerminal, type SyncJobStatus } from '../../../types/api';

export interface UseSyncJobResult {
  folderPath: string;
  setFolderPath: (value: string) => void;
  submit: () => void;
  isSubmitting: boolean;
  jobId: string | null;
  acceptedMessage: string | null;
  status: SyncJobStatus | null;
  error: string | null;
  isPolling: boolean;
  reset: () => void;
}

/**
 * Queues a folder reconciliation job and then polls
 * `GET /recruitment/sync/{jobId}` until the job reports a terminal state.
 * Polling stops on `Completed`/`Failed`, on unmount, and when the job is reset.
 */
export function useSyncJob(): UseSyncJobResult {
  const toast = useToast();

  const [folderPath, setFolderPath] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [jobId, setJobId] = useState<string | null>(null);
  const [acceptedMessage, setAcceptedMessage] = useState<string | null>(null);
  const [status, setStatus] = useState<SyncJobStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isPolling, setIsPolling] = useState(false);

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
    controllerRef.current?.abort();
    controllerRef.current = null;
    setJobId(null);
    setAcceptedMessage(null);
    setStatus(null);
    setError(null);
    setIsPolling(false);
    setIsSubmitting(false);
  }, []);

  const submit = useCallback((): void => {
    const trimmed = folderPath.trim();
    if (trimmed.length === 0) {
      setError('Enter the absolute folder path that contains the resumes.');
      return;
    }

    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    setIsSubmitting(true);
    setError(null);
    setStatus(null);
    setJobId(null);
    setAcceptedMessage(null);

    startSync({ folderPath: trimmed }, { signal: controller.signal })
      .then((accepted) => {
        if (!isMountedRef.current) return;
        setJobId(accepted.jobId);
        setAcceptedMessage(accepted.message);
        setIsPolling(true);
        toast.info(accepted.message);
      })
      .catch((caught: unknown) => {
        if (isAbortError(caught) || !isMountedRef.current) return;
        const message = toUserMessage(caught, 'Could not start the folder sync.');
        setError(message);
        toast.error(message);
      })
      .finally(() => {
        if (isMountedRef.current) setIsSubmitting(false);
      });
  }, [folderPath, toast]);

  // Poll the job while it is queued or running.
  useEffect(() => {
    if (jobId === null) return;

    let cancelled = false;
    const controller = new AbortController();

    const tick = async (): Promise<void> => {
      try {
        const next = await getSyncJob(jobId, { signal: controller.signal });
        if (cancelled || !isMountedRef.current) return;
        setStatus(next);
        if (isSyncJobTerminal(next.state)) {
          setIsPolling(false);
          if (next.state === 'Completed') {
            toast.success(`Folder sync completed: ${next.discovered} discovered, ${next.skipped} skipped, ${next.failed} failed.`);
          } else {
            setError(next.error ?? 'The folder sync failed.');
            toast.error(next.error ?? 'The folder sync failed.');
          }
        }
      } catch (caught) {
        if (cancelled || isAbortError(caught) || !isMountedRef.current) return;
        const message = toUserMessage(caught, 'Lost track of the sync job.');
        setError(message);
        toast.error(message);
        setIsPolling(false);
      }
    };

    void tick();
    const timer = window.setInterval(() => {
      if (document.hidden) return;
      void tick();
    }, Math.max(env.pollIntervalMs, 500));

    return () => {
      cancelled = true;
      controller.abort();
      window.clearInterval(timer);
    };
  }, [jobId, toast]);

  return {
    folderPath,
    setFolderPath,
    submit,
    isSubmitting,
    jobId,
    acceptedMessage,
    status,
    error,
    isPolling,
    reset,
  };
}
