import { useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, ChevronDown, ChevronRight, FileText, Trash2, X } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { Spinner } from '../../../components/ui/Spinner';
import { StatusBadge } from '../../../components/ui/StatusBadge';
import { cn } from '../../../lib/cn';
import { formatDateTime } from '../../../lib/formatDate';
import { toUserMessage } from '../../../services/httpClient';
import type { ResumeDetail, ResumeSummary } from '../../../types/api';
import { useClickOutside } from '../../../hooks/useClickOutside';

export interface ResumeTableProps {
  items: readonly ResumeSummary[];
  isLoading: boolean;
  expandedId: string | null;
  detail: ResumeDetail | null;
  detailError: Error | null;
  isDetailLoading: boolean;
  deletingId: string | null;
  onToggle: (resumeId: string) => void;
  onDelete: (resume: ResumeSummary) => Promise<boolean>;
  emptyState: ReactNode;
}

function DeleteConfirm({ resume, onCancel, onConfirm }: { resume: ResumeSummary; onCancel: () => void; onConfirm: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useClickOutside(ref, onCancel);

  return (
    <div className="confirm-popover" ref={ref} role="alertdialog" aria-label="Confirm delete">
      <p className="confirm-popover__text">
        Remove <strong>{resume.candidateName}</strong> from the index?
        <span className="confirm-popover__sub">The source file on disk is kept.</span>
      </p>
      <div className="confirm-popover__actions">
        <Button size="sm" variant="ghost" icon={<X size={13} aria-hidden="true" />} onClick={onCancel}>
          Cancel
        </Button>
        <Button size="sm" variant="danger" icon={<Trash2 size={13} aria-hidden="true" />} onClick={onConfirm}>
          Delete
        </Button>
      </div>
    </div>
  );
}

function ChunkList({ detail, isLoading, error }: { detail: ResumeDetail | null; isLoading: boolean; error: Error | null }) {
  if (isLoading) {
    return (
      <div className="chunk-panel">
        <Spinner size={16} label="Loading chunks" />
        <span className="chunk-panel__note">Loading chunks…</span>
      </div>
    );
  }

  if (error !== null) {
    return (
      <div className="chunk-panel chunk-panel--error" role="alert">
        <AlertTriangle size={15} aria-hidden="true" />
        {toUserMessage(error, 'Could not load chunks.')}
      </div>
    );
  }

  if (detail === null || detail.chunks.length === 0) {
    return (
      <div className="chunk-panel">
        <span className="chunk-panel__note">This resume has no stored chunks yet.</span>
      </div>
    );
  }

  return (
    <div className="chunk-panel">
      <ul className="chunk-list">
        {detail.chunks.map((chunk) => (
          <li key={chunk.id} className="chunk-item">
            <div className="chunk-item__head">
              <span className="chunk-item__index">#{chunk.chunkIndex}</span>
              <span className="chunk-item__meta">{chunk.characterCount} chars</span>
              <span className={cn('chunk-item__embed', chunk.hasEmbedding && 'chunk-item__embed--on')}>
                {chunk.hasEmbedding ? 'embedded' : 'not embedded'}
              </span>
            </div>
            <p className="chunk-item__text">{chunk.textContent}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function ResumeTable({
  items,
  isLoading,
  expandedId,
  detail,
  detailError,
  isDetailLoading,
  deletingId,
  onToggle,
  onDelete,
  emptyState,
}: ResumeTableProps) {
  const [requestedConfirmId, setRequestedConfirmId] = useState<string | null>(null);
  // Derived during render: a confirm popover can never outlive its row.
  const confirmingId = requestedConfirmId !== null && items.some((item) => item.id === requestedConfirmId) ? requestedConfirmId : null;

  if (isLoading && items.length === 0) {
    return (
      <div className="table-loading">
        <Spinner size={20} label="Loading resumes" />
        <span>Loading the resume registry…</span>
      </div>
    );
  }

  if (items.length === 0) return <>{emptyState}</>;

  return (
    <div className="resume-table-wrap">
      <table className="resume-table">
        <thead>
          <tr>
            <th scope="col">Candidate</th>
            <th scope="col">Status</th>
            <th scope="col">Chunks</th>
            <th scope="col">Path</th>
            <th scope="col">Last Synced</th>
            <th scope="col">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((resume) => {
            const isExpanded = expandedId === resume.id;
            return (
              <tr
                key={resume.id}
                className={cn('resume-row', isExpanded && 'resume-row--expanded')}
                onClick={() => onToggle(resume.id)}
                tabIndex={0}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    onToggle(resume.id);
                  }
                }}
                aria-expanded={isExpanded}
              >
                <td className="td-name">
                  <span className="td-name__label">
                    {isExpanded ? <ChevronDown size={14} aria-hidden="true" /> : <ChevronRight size={14} aria-hidden="true" />}
                    <FileText size={14} aria-hidden="true" className="td-name__icon" />
                    <span className="td-name__text" title={resume.candidateName}>
                      {resume.candidateName}
                    </span>
                  </span>
                  {resume.failureReason ? (
                    <span className="td-failure" title={resume.failureReason}>
                      {resume.failureReason}
                    </span>
                  ) : null}
                </td>
                <td>
                  <StatusBadge status={resume.status} />
                </td>
                <td className="td-numbers">
                  {resume.chunkCount} <span className="td-numbers__sub">/ {resume.embeddedChunkCount} embedded</span>
                </td>
                <td className="td-path" title={resume.filePath}>
                  {resume.filePath}
                </td>
                <td className="td-date">{formatDateTime(resume.lastSyncedAt)}</td>
                <td className="td-actions" onClick={(event) => event.stopPropagation()}>
                  {confirmingId === resume.id ? (
                    <DeleteConfirm
                      resume={resume}
                      onCancel={() => setRequestedConfirmId(null)}
                      onConfirm={() => {
                        setRequestedConfirmId(null);
                        void onDelete(resume);
                      }}
                    />
                  ) : (
                    <Button
                      size="sm"
                      variant="ghost"
                      className="icon-btn"
                      title={`Delete ${resume.candidateName}`}
                      aria-label={`Delete ${resume.candidateName}`}
                      icon={<Trash2 size={14} aria-hidden="true" />}
                      loading={deletingId === resume.id}
                      onClick={() => setRequestedConfirmId(resume.id)}
                    />
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {expandedId !== null ? (
        <div className="resume-detail">
          <ChunkList detail={detail} isLoading={isDetailLoading} error={detailError} />
        </div>
      ) : null}
    </div>
  );
}
