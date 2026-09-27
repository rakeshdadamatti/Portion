import { postJson, request } from './httpClient';
import type { SyncJobAccepted, SyncJobStatus, SyncRequest } from '../types/api';

const BASE = '/recruitment/sync';

/** `POST /api/v1/recruitment/sync` — queues a folder reconciliation job. */
export function startSync(body: SyncRequest, options: { signal?: AbortSignal | null } = {}): Promise<SyncJobAccepted> {
  return postJson<SyncJobAccepted>(BASE, body, { signal: options.signal ?? null });
}

/** `GET /api/v1/recruitment/sync/{jobId}` — 404 for an unknown job. */
export function getSyncJob(jobId: string, options: { signal?: AbortSignal | null } = {}): Promise<SyncJobStatus> {
  return request<SyncJobStatus>(`${BASE}/${encodeURIComponent(jobId)}`, { signal: options.signal ?? null });
}
