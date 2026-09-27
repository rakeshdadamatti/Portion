import { CheckCircle2, Circle, Loader2 } from 'lucide-react';
import { cn } from '../../../lib/cn';
import { SCREENING_STAGES, type ScreeningStage } from '../../../types/api';

const STAGE_LABELS: Record<ScreeningStage, string> = {
  embedding: 'Embedding your prompt',
  search: 'Searching the vector store',
  fallback: 'Falling back to keyword search',
  generating: 'Generating the answer',
  complete: 'Answer complete',
};

const ACTIVE_INDEX: Record<ScreeningStage, number> = {
  embedding: 0,
  search: 1,
  fallback: 1,
  generating: 2,
  complete: 3,
};

export interface PipelineStatusProps {
  stage: ScreeningStage | null;
  isStreaming: boolean;
  elapsedMs: number | null;
}

function StepIcon({ state }: { state: 'done' | 'active' | 'pending' }) {
  if (state === 'done') return <CheckCircle2 size={13} aria-hidden="true" />;
  if (state === 'active') return <Loader2 size={13} className="spin" aria-hidden="true" />;
  return <Circle size={11} aria-hidden="true" />;
}

/**
 * Single line describing exactly where the SSE pipeline currently is. Steps are
 * only shown as "done" once the stream has emitted a later stage, so the line can
 * never claim progress the server has not reported.
 */
export function PipelineStatus({ stage, isStreaming, elapsedMs }: PipelineStatusProps) {
  if (stage === null && !isStreaming && elapsedMs === null) return null;

  const activeIndex = stage === null ? -1 : ACTIVE_INDEX[stage];
  const lastIndex = SCREENING_STAGES.length - 1;
  const visibleStages = SCREENING_STAGES.slice(0, lastIndex);

  return (
    <div className="pipeline-status" role="status" aria-live="polite">
      <ol className="pipeline-steps">
        {visibleStages.map((step, index) => {
          const state = stage === null ? 'pending' : index < activeIndex ? 'done' : index === activeIndex ? 'active' : 'pending';
          return (
            <li key={step} className={cn('pipeline-step', `pipeline-step--${state}`)}>
              <StepIcon state={state} />
              <span className="pipeline-step__label">{STAGE_LABELS[step]}</span>
            </li>
          );
        })}
      </ol>
      {elapsedMs !== null ? <span className="pipeline-status__elapsed">{(elapsedMs / 1000).toFixed(2)}s</span> : null}
    </div>
  );
}
