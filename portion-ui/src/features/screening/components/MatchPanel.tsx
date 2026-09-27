import { SearchX } from 'lucide-react';
import { CandidateCard } from '../../../components/candidate/CandidateCard';
import { MatchChartCaption, MatchScoreChart } from '../../../components/candidate/MatchScoreChart';
import { EmptyState } from '../../../components/ui/EmptyState';
import { useTheme } from '../../../app/providers/ThemeProvider';
import type { ResumeSummary, ScreeningMatchesEvent } from '../../../types/api';

export interface MatchPanelProps {
  matches: ScreeningMatchesEvent | null;
  /** Registry rows keyed by resume id, used only to enrich cards with real metadata. */
  resumesById: ReadonlyMap<string, ResumeSummary>;
}

/**
 * Renders the `matches` SSE payload. With no payload it shows an explicit empty
 * state — it never fabricates a score to fill the space.
 */
export function MatchPanel({ matches, resumesById }: MatchPanelProps) {
  const { theme } = useTheme();
  const candidates = matches?.candidates ?? [];

  return (
    <section className="panel match-panel" aria-labelledby="match-panel-title">
      <header className="panel-header">
        <div className="panel-heading">
          <h2 className="panel-title" id="match-panel-title">
            Match Results
          </h2>
          <p className="panel-desc">
            Semantic candidates returned by the vector search for your last prompt.
          </p>
        </div>
        {matches !== null ? (
          <div className="panel-actions">
            <span className="match-panel__count">
              {candidates.length} candidate{candidates.length === 1 ? '' : 's'} · top {matches.topChunkCount} chunks
            </span>
          </div>
        ) : null}
      </header>

      <div className="panel-body">
        {candidates.length === 0 ? (
          <EmptyState
            icon={<SearchX size={26} aria-hidden="true" />}
            title="No match data yet"
            description="Run a screening prompt — the server streams real match scores here as soon as the vector search completes."
          />
        ) : (
          <>
            <MatchScoreChart candidates={candidates} theme={theme} />
            <MatchChartCaption candidates={candidates} />
            <div className="candidates-row">
              {candidates.map((candidate) => (
                <CandidateCard
                  key={candidate.resumeId}
                  match={candidate}
                  resume={resumesById.get(candidate.resumeId)}
                />
              ))}
            </div>
          </>
        )}
      </div>
    </section>
  );
}
