import {
  request,
  requestVoid,
  type HttpRequestOptions,
} from './httpClient';
import type {
  DeleteResumeOptions,
  PagedResult,
  ResumeDetail,
  ResumeSummary,
  UploadResumeResponse,
} from '../types/api';

const BASE = '/recruitment';
export const MAX_PAGE_SIZE = 200;
export const DEFAULT_PAGE_SIZE = 25;

export const RESUME_FILE_ACCEPT = '.pdf,.docx,.txt';
export const RESUME_FILE_EXTENSIONS = ['pdf', 'docx', 'txt'] as const;

export interface ListResumesQuery {
  page?: number;
  pageSize?: number;
  q?: string;
  signal?: AbortSignal | null;
}

/** `GET /api/v1/recruitment/resumes` — paged registry listing. */
export function listResumes(query: ListResumesQuery = {}): Promise<PagedResult<ResumeSummary>> {
  const { page = 1, pageSize = DEFAULT_PAGE_SIZE, q = '', signal = null } = query;
  return request<PagedResult<ResumeSummary>>(`${BASE}/resumes`, {
    query: { page, pageSize, q },
    signal,
  });
}

/** `GET /api/v1/recruitment/resumes/{id}` — summary plus every stored chunk. */
export function getResume(resumeId: string, options: Pick<HttpRequestOptions, 'signal'> = {}): Promise<ResumeDetail> {
  return request<ResumeDetail>(`${BASE}/resumes/${encodeURIComponent(resumeId)}`, options);
}

/** `POST /api/v1/recruitment/resumes` — multipart upload, field name `file`. */
export function uploadResume(file: File, options: Pick<HttpRequestOptions, 'signal'> = {}): Promise<UploadResumeResponse> {
  const form = new FormData();
  form.append('file', file, file.name);
  return request<UploadResumeResponse>(`${BASE}/resumes`, {
    method: 'POST',
    body: form,
    accept: 'application/json',
    signal: options.signal,
  });
}

/** `DELETE /api/v1/recruitment/resumes/{id}?deleteFile=true` — 204. */
export function deleteResume(resumeId: string, options: DeleteResumeOptions & { signal?: AbortSignal | null }): Promise<void> {
  return requestVoid(`${BASE}/resumes/${encodeURIComponent(resumeId)}`, {
    method: 'DELETE',
    query: { deleteFile: options.deleteFile },
    signal: options.signal ?? null,
  });
}

/** Client-side guard mirroring the server's accepted extensions. */
export function isSupportedResumeFile(file: File): boolean {
  const dotIndex = file.name.lastIndexOf('.');
  if (dotIndex < 0) return false;
  const extension = file.name.slice(dotIndex + 1).toLowerCase();
  return (RESUME_FILE_EXTENSIONS as readonly string[]).includes(extension);
}
