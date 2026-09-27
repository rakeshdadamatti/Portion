import { AlertTriangle, FolderSync, Loader2, RotateCcw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { EmptyState } from '../../components/ui/EmptyState';
import { Panel } from '../../components/ui/Panel';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { TextField } from '../../components/ui/TextField';
import { formatDateTime } from '../../lib/formatDate';
import type { SyncJobStatus } from '../../types/api';
import { useSyncJob } from './hooks/useSyncJob';

const STAT_FIELDS = [
  { key: 'discovered', label: 'Discovered' },
  { key: 'skipped', label: 'Skipped' },
  { key: 'queued', label: 'Queued' },
  { key: 'failed', label: 'Failed' },
] as const satisfies ReadonlyArray<{ key: keyof SyncJobStatus; label: string }>;

function StatsGrid({ status }: { status: SyncJobStatus }) {
  return (
    <div className="stats-grid">
      {STAT_FIELDS.map((field) => (
        <div key={field.key} className="stat">
          <span className="stat__label">{field.label}</span>
          <span className="stat__value">{status[field.key]}</span>
        </div>
      ))}
    </div>
  );
}

export function SyncRepositoryPage() {
  const job = useSyncJob();
  const isRunning = job.isPolling || job.isSubmitting;
  const fieldError = job.error !== null && job.jobId === null ? job.error : undefined;

  return (
    <Panel
      variant="narrow"
      title="Reconciliation Bulk Sync"
      description="Point the API at a local folder. SHA-256 hashes are used to skip resumes that are already indexed."
    >
      <form
        className="sync-form"
        onSubmit={(event) => {
          event.preventDefault();
          job.submit();
        }}
      >
        <TextField
          label="Folder path"
          placeholder="C:\Users\ashay\Documents\Resumes"
          value={job.folderPath}
          onChange={(event) => job.setFolderPath(event.target.value)}
          error={fieldError}
          hint="Absolute path on the machine running the Portion API."
          disabled={isRunning}
          autoComplete="off"
          spellCheck={false}
        />
        <div className="sync-actions">
          <Button
            type="submit"
            variant="primary"
            icon={<FolderSync size={16} aria-hidden="true" />}
            loading={job.isSubmitting}
            disabled={isRunning || job.folderPath.trim().length === 0}
          >
            {isRunning ? 'Reconciling…' : 'Reconcile Folder'}
          </Button>
          <Button
            type="button"
            variant="ghost"
            icon={<RotateCcw size={15} aria-hidden="true" />}
            onClick={job.reset}
            disabled={job.jobId === null && job.error === null}
          >
            Reset
          </Button>
        </div>
      </form>

      {isRunning && job.status === null ? (
        <div className="sync-pending" role="status">
          <Loader2 size={16} className="spin" aria-hidden="true" />
          <span>Waiting for the job to report progress…</span>
        </div>
      ) : null}

      {job.jobId !== null ? (
        <div className="sync-status">
          <div className="sync-status__head">
            <div>
              <p className="sync-status__title">
                Job <code>{job.jobId}</code>
              </p>
              <p className="sync-status__path" title={job.status?.folderPath ?? job.folderPath}>
                {job.status?.folderPath ?? job.folderPath}
              </p>
            </div>
            {job.status !== null ? <StatusBadge status={job.status.state} /> : null}
          </div>

          {job.status !== null ? <StatsGrid status={job.status} /> : null}

          {job.status !== null ? (
            <dl className="sync-status__times">
              <div>
                <dt>Started</dt>
                <dd>{formatDateTime(job.status.startedAt)}</dd>
              </div>
              <div>
                <dt>Completed</dt>
                <dd>{formatDateTime(job.status.completedAt)}</dd>
              </div>
            </dl>
          ) : null}

          {job.acceptedMessage !== null ? <p className="sync-status__accepted">{job.acceptedMessage}</p> : null}

          {job.error !== null && job.status !== null ? (
            <p className="sync-error" role="alert">
              <AlertTriangle size={15} aria-hidden="true" />
              {job.error}
            </p>
          ) : null}
        </div>
      ) : job.error === null ? (
        <EmptyState
          icon={<FolderSync size={26} aria-hidden="true" />}
          title="No sync job yet"
          description="Submit a folder path to queue a reconciliation job. Progress is polled from the API until the job finishes."
        />
      ) : null}
    </Panel>
  );
}
