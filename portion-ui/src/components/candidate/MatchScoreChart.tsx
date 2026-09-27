import { useMemo } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  LabelList,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { ScreeningMatchCandidate } from '../../types/api';
import { formatScore } from '../../lib/formatScore';
import type { Theme } from '../../app/providers/ThemeProvider';

export interface MatchScoreChartProps {
  candidates: readonly ScreeningMatchCandidate[];
  theme: Theme;
  height?: number;
}

const AXIS_WIDTH = 118;
const LABEL_WIDTH = 52;
const NAME_LIMIT = 18;

function truncate(name: string): string {
  return name.length > NAME_LIMIT ? `${name.slice(0, NAME_LIMIT - 1)}…` : name;
}

/**
 * Horizontal bar chart of the real `score` values the backend sends on the
 * `matches` SSE event. The axis is pinned to 0..1 so bar lengths are directly
 * comparable and never inflated by an auto-fitted domain.
 *
 * Rendered with `aria-hidden` because the adjacent candidate cards expose the
 * exact same numbers to assistive technology in text form.
 */
export function MatchScoreChart({ candidates, theme, height = 220 }: MatchScoreChartProps) {
  const data = useMemo(
    () =>
      candidates.map((candidate) => ({
        resumeId: candidate.resumeId,
        name: truncate(candidate.candidateName),
        score: candidate.score,
      })),
    [candidates],
  );

  const gridStroke = 'var(--chart-grid)';
  const axisFill = 'var(--chart-axis)';
  const barFill = theme === 'dark' ? 'var(--chart-bar)' : 'var(--chart-bar)';

  return (
    <div className="match-chart" style={{ height }} aria-hidden="true">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: LABEL_WIDTH, bottom: 4, left: 0 }} barCategoryGap="28%">
          <CartesianGrid stroke={gridStroke} horizontal={false} />
          <XAxis
            type="number"
            domain={[0, 1]}
            ticks={[0, 0.25, 0.5, 0.75, 1]}
            tickFormatter={(value: number) => formatScore(value)}
            stroke={gridStroke}
            tick={{ fill: axisFill, fontSize: 10 }}
            tickLine={false}
          />
          <YAxis
            type="category"
            dataKey="name"
            width={AXIS_WIDTH}
            stroke={gridStroke}
            tick={{ fill: axisFill, fontSize: 11 }}
            tickLine={false}
          />
          <Tooltip
            cursor={{ fill: 'var(--accent-dim)' }}
            formatter={(value) => [formatScore(Number(value)), 'Match score']}
            contentStyle={{
              background: 'var(--bg-surface)',
              border: '1px solid var(--border)',
              borderRadius: 'var(--radius-sm)',
              color: 'var(--text-primary)',
              fontSize: 12,
            }}
            labelStyle={{ color: 'var(--text-secondary)' }}
          />
          <Bar dataKey="score" fill={barFill} radius={[0, 4, 4, 0]} isAnimationActive={false}>
            {data.map((entry) => (
              <Cell key={entry.resumeId} fill={barFill} />
            ))}
            <LabelList
              dataKey="score"
              position="right"
              offset={8}
              formatter={(value) => formatScore(Number(value))}
              style={{ fill: 'var(--text-secondary)', fontSize: 11, fontWeight: 600 }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

export function MatchChartCaption({ candidates }: { candidates: readonly ScreeningMatchCandidate[] }) {
  const best = candidates.reduce((max, candidate) => Math.max(max, candidate.score), Number.NEGATIVE_INFINITY);
  const worst = candidates.reduce((min, candidate) => Math.min(min, candidate.score), Number.POSITIVE_INFINITY);

  return (
    <p className="match-chart__caption">
      Cosine similarity per candidate, straight from the vector search. Best {formatScore(best)} · weakest{' '}
      {formatScore(worst)} across {candidates.length} scored {candidates.length === 1 ? 'candidate' : 'candidates'}.
    </p>
  );
}
