import { Target, Timer } from 'lucide-react';
import { StatusBadge } from '../ui/StatusBadge';
import { cn } from '../../lib/cn';
import { formatDateTime } from '../../lib/formatDate';
import { initials } from '../../lib/initials';
import { formatScore } from '../../lib/formatScore';
import type { ResumeSummary, ScreeningMatchCandidate } from '../../types/api';

export interface CandidateCardProps {
  /** Real record from the `matches` SSE event — the only source of the score. */
  match: ScreeningMatchCandidate;
  /**
   * Optional enrichment from the registry endpoint. When absent the card simply
   * omits status / last-synced rather than guessing them.
   */
  resume?: ResumeSummary;
  className?: string;
}

/**
 * Candidate summary built exclusively from API-returned data: name and match
 * score from the screening stream, status / chunk counts / last-synced from the
 * resume registry. There is no derived or synthesised score anywhere here.
 */
export function CandidateCard({ match, resume, className }: CandidateCardProps) {
  const name = resume?.candidateName ?? match.candidateName;

  return (
    <article className={cn('candidate-card', className)}>
      <div className="candidate-card__left">
        <div className="candidate-avatar" aria-hidden="true">
          {initials(name)}
        </div>
        <div className="candidate-info">
          <h3 className="candidate-name" title={name}>
            {name}
          </h3>
          <div className="candidate-meta">
            <StatusBadge status={resume?.status ?? ''} />
            <span className="candidate-meta__item" title="Matching chunks returned by the vector search">
              {match.chunkCount} matched {match.chunkCount === 1 ? 'chunk' : 'chunks'}
            </span>
            {resume !== undefined ? (
              <span className="candidate-meta__item">
                {resume.chunkCount} indexed · {resume.embeddedChunkCount} embedded
              </span>
            ) : null}
          </div>
          {resume !== undefined ? (
            <p className="candidate-synced">
              <Timer size={12} aria-hidden="true" />
              <span>
                Last synced <time dateTime={resume.lastSyncedAt}>{formatDateTime(resume.lastSyncedAt)}</time>
              </span>
            </p>
          ) : null}
          {resume?.failureReason ? <p className="candidate-failure">{resume.failureReason}</p> : null}
        </div>
      </div>

      <div className="candidate-card__right">
        <div className="match-badge" title="Cosine similarity returned by the vector search">
          <Target size={13} aria-hidden="true" />
          {formatScore(match.score)}
        </div>
      </div>
    </article>
  );
}
