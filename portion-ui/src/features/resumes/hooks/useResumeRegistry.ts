import { useCallback, useEffect, useMemo, useState } from 'react';
import { useToast } from '../../../app/providers/ToastProvider';
import { env } from '../../../config/env';
import { useAsync } from '../../../hooks/useAsync';
import { usePolling } from '../../../hooks/usePolling';
import { toUserMessage } from '../../../services/httpClient';
import { deleteResume, getResume, listResumes } from '../../../services/resumeApi';
import type { PagedResult, ResumeDetail, ResumeSummary } from '../../../types/api';

const PAGE_SIZE = 25;

export interface UseResumeRegistryResult {
  items: readonly ResumeSummary[];
  page: number;
  pageSize: number;
  totalPages: number;
  totalCount: number;
  isLoading: boolean;
  error: Error | null;
  /** True while a `Processing` row is present and the interval is armed. */
  isLive: boolean;
  /** True while an individual refresh request is in flight. */
  isRefreshing: boolean;
  hasProcessing: boolean;
  searchDraft: string;
  setSearchDraft: (value: string) => void;
  submitSearch: () => void;
  goToPage: (page: number) => void;
  refresh: () => Promise<void>;
  expandedId: string | null;
  detail: ResumeDetail | null;
  detailError: Error | null;
  isDetailLoading: boolean;
  toggleExpanded: (resumeId: string) => void;
  remove: (resume: ResumeSummary) => Promise<boolean>;
  deletingId: string | null;
}

/**
 * Drives the resume registry: paged listing, server-side search (`q`), chunk
 * expansion, deletion and auto-refresh.
 *
 * Auto-refresh is armed **only** while at least one row is in `Processing`; a
 * settled registry issues no background requests. Page and search changes load
 * explicitly.
 */
export function useResumeRegistry() {
  const toast = useToast();

  const [page, setPage] = useState(1);
  const [query, setQuery] = useState('');
  const [searchDraft, setSearchDraft] = useState('');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const reportError = useCallback(
    (fallback: string) => (error: Error) => {
      toast.error(toUserMessage(error, fallback));
    },
    [toast],
  );

  const fetchPage = useCallback(
    (nextPage: number, search: string): Promise<PagedResult<ResumeSummary>> =>
      listResumes({ page: nextPage, pageSize: PAGE_SIZE, q: search }),
    [],
  );

  const { data: result, error, isLoading, run: runPage } = useAsync(fetchPage, {
    onError: reportError('Could not load the resume registry.'),
  });

  // Page / search changes load explicitly. The interval below never double-fires
  // on mount because `immediate` is left off.
  useEffect(() => {
    void runPage(page, query);
  }, [page, query, runPage]);

  const hasProcessing = useMemo(() => (result?.items ?? []).some((item) => item.status === 'Processing'), [result]);

  const { refresh, isPolling, isRefreshing } = usePolling(async () => {
    await runPage(page, query);
  }, {
    intervalMs: env.pollIntervalMs,
    enabled: hasProcessing,
    immediate: false,
    onError: reportError('Auto-refresh of the resume registry failed.'),
  });

  const loadDetail = useCallback((resumeId: string): Promise<ResumeDetail> => getResume(resumeId), []);

  const {
    data: loadedDetail,
    error: detailError,
    isLoading: isDetailLoading,
    run: runDetail,
    reset: resetDetail,
  } = useAsync(loadDetail, { onError: reportError('Could not load the chunks for that resume.') });

  // Derived during render: a collapsed row never shows a stale chunk list.
  const detail = expandedId === null ? null : loadedDetail;

  const collapse = useCallback(() => {
    setExpandedId(null);
    resetDetail();
  }, [resetDetail]);

  const submitSearch = useCallback(() => {
    collapse();
    setPage(1);
    setQuery(searchDraft.trim());
  }, [collapse, searchDraft]);

  const goToPage = useCallback(
    (nextPage: number) => {
      collapse();
      setPage(Math.max(1, nextPage));
    },
    [collapse],
  );

  const toggleExpanded = useCallback(
    (resumeId: string) => {
      if (expandedId === resumeId) {
        collapse();
        return;
      }
      setExpandedId(resumeId);
      void runDetail(resumeId);
    },
    [collapse, expandedId, runDetail],
  );

  const remove = useCallback(
    async (resume: ResumeSummary): Promise<boolean> => {
      setDeletingId(resume.id);
      try {
        await deleteResume(resume.id, { deleteFile: false });
        if (expandedId === resume.id) collapse();
        const refreshed = await runPage(page, query);
        if (refreshed !== null && page > refreshed.totalPages && refreshed.totalPages > 0) setPage(refreshed.totalPages);
        toast.success(`Deleted the indexed copy of “${resume.candidateName}”.`);
        return true;
      } catch (caught) {
        toast.error(toUserMessage(caught, 'Could not delete that resume.'));
        return false;
      } finally {
        setDeletingId(null);
      }
    },
    [collapse, expandedId, page, query, runPage, toast],
  );

  return {
    items: result?.items ?? [],
    page,
    pageSize: result?.pageSize ?? PAGE_SIZE,
    totalPages: result?.totalPages ?? 0,
    totalCount: result?.totalCount ?? 0,
    isLoading,
    error,
    isLive: isPolling,
    isRefreshing,
    hasProcessing,
    searchDraft,
    setSearchDraft,
    submitSearch,
    goToPage,
    refresh,
    expandedId,
    detail,
    detailError,
    isDetailLoading,
    toggleExpanded,
    remove,
    deletingId,
  };
}
