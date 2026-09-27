import { useState } from 'react';
import { ChevronLeft, ChevronRight, RadioTower, RefreshCw, Search, UploadCloud } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button } from '../../components/ui/Button';
import { EmptyState } from '../../components/ui/EmptyState';
import { Panel } from '../../components/ui/Panel';
import { toUserMessage } from '../../services/httpClient';
import { ResumeTable } from './components/ResumeTable';
import { useResumeRegistry } from './hooks/useResumeRegistry';

export function ResumeRegistryPage() {
  const registry = useResumeRegistry();
  const [isRefreshing, setIsRefreshing] = useState(false);

  const handleRefresh = async (): Promise<void> => {
    setIsRefreshing(true);
    try {
      await registry.refresh();
    } finally {
      setIsRefreshing(false);
    }
  };

  const { page, totalPages, totalCount } = registry;

  return (
    <Panel
      title="Indexed Resume Registry"
      description="Live view of every resume the API has indexed, with chunk and embedding counts."
      actions={
        <>
          {registry.hasProcessing ? (
            <span className="live-badge" title="Auto-refreshing while a resume is Processing">
              <RadioTower size={13} aria-hidden="true" />
              Live · auto-refreshing
            </span>
          ) : null}
          <Button icon={<RefreshCw size={15} aria-hidden="true" />} onClick={() => void handleRefresh()} loading={isRefreshing}>
            Refresh
          </Button>
        </>
      }
    >
      <form
        className="registry-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          registry.submitSearch();
        }}
      >
        <div className="registry-search">
          <Search size={15} aria-hidden="true" />
          <input
            className="registry-search__input"
            type="search"
            value={registry.searchDraft}
            onChange={(event) => registry.setSearchDraft(event.target.value)}
            placeholder="Search candidate, file name or path…"
            aria-label="Search the resume registry"
          />
        </div>
        <Button type="submit" variant="primary" icon={<Search size={14} aria-hidden="true" />}>
          Search
        </Button>
      </form>

      {registry.error !== null ? (
        <p className="inline-error" role="alert">
          {toUserMessage(registry.error, 'Could not load the resume registry.')}
        </p>
      ) : null}

      <ResumeTable
        items={registry.items}
        isLoading={registry.isLoading}
        expandedId={registry.expandedId}
        detail={registry.detail}
        detailError={registry.detailError}
        isDetailLoading={registry.isDetailLoading}
        deletingId={registry.deletingId}
        onToggle={registry.toggleExpanded}
        onDelete={registry.remove}
        emptyState={
          <EmptyState
            icon={<UploadCloud size={26} aria-hidden="true" />}
            title="No resumes indexed yet"
            description="Upload a candidate CV directly, or point the API at a folder to reconcile in bulk."
            action={
              <Link className="btn btn--primary btn--md" to="/upload">
                <span className="btn__label">Upload a resume</span>
              </Link>
            }
          />
        }
      />

      <footer className="registry-footer">
        <span className="registry-footer__count">
          {totalCount} resume{totalCount === 1 ? '' : 's'} · page {page} of {Math.max(totalPages, 1)}
        </span>
        <div className="pager">
          <Button
            size="sm"
            variant="subtle"
            icon={<ChevronLeft size={14} aria-hidden="true" />}
            disabled={page <= 1}
            onClick={() => registry.goToPage(page - 1)}
          >
            Previous
          </Button>
          <Button
            size="sm"
            variant="subtle"
            disabled={page >= totalPages}
            onClick={() => registry.goToPage(page + 1)}
          >
            Next
            <ChevronRight size={14} aria-hidden="true" />
          </Button>
        </div>
      </footer>
    </Panel>
  );
}
