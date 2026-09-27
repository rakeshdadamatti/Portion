import { useRef, useState } from 'react';
import { ChevronDown, Lightbulb, History, Square, XCircle } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { QuickPromptMenu } from './QuickPromptMenu';
import { SCREENING_MAX_PROMPT_LENGTH } from '../../../types/api';
import { cn } from '../../../lib/cn';

const EXAMPLE_QUERIES = [
  'Compare my candidates for a Lead .NET position',
  'Who has experience with microservices?',
  'Find candidates skilled in Polly and resilience patterns',
] as const;

const JOB_DESCRIPTIONS = [
  'Senior .NET Developer – 5+ yrs, microservices, Azure',
  'Backend Engineer – REST APIs, PostgreSQL, Docker',
] as const;

export interface PromptComposerProps {
  isStreaming: boolean;
  onSend: (prompt: string) => void;
  onCancel: () => void;
}

/** Text entry for the screening pipeline: Enter sends, Shift+Enter is not captured. */
export function PromptComposer({ isStreaming, onSend, onCancel }: PromptComposerProps) {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  const trimmedLength = value.trim().length;
  const tooLong = trimmedLength > SCREENING_MAX_PROMPT_LENGTH;
  const canSend = trimmedLength > 0 && !tooLong && !isStreaming;

  const submit = (): void => {
    if (!canSend) return;
    onSend(value);
    setValue('');
  };

  return (
    <div className="chat-input-area">
      <div className="chat-input-row">
        <div className="input-wrapper">
          <input
            ref={inputRef}
            className="chat-input"
            type="text"
            value={value}
            onChange={(event) => setValue(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                submit();
              }
            }}
            placeholder="Explore skills, experience, and requirements…"
            disabled={isStreaming}
            aria-label="Screening prompt"
            aria-invalid={tooLong || undefined}
            aria-describedby="composer-hint"
            maxLength={SCREENING_MAX_PROMPT_LENGTH + 500}
          />
          <span className="input-hint" id="composer-hint">
            e.g. &ldquo;Compare my candidates for a Lead .NET position, emphasizing Polly &amp; Microservices.&rdquo;
          </span>
        </div>

        <div className="input-actions">
          {isStreaming ? (
            <Button variant="danger" icon={<Square size={14} aria-hidden="true" />} onClick={onCancel}>
              Stop
            </Button>
          ) : (
            <Button
              variant="primary"
              className="send-btn"
              onClick={submit}
              disabled={!canSend}
              title={tooLong ? `Prompt exceeds ${SCREENING_MAX_PROMPT_LENGTH} characters` : undefined}
            >
              Send
            </Button>
          )}
        </div>
      </div>

      <div className="composer-footer">
        <div className="pill-row">
          <QuickPromptMenu
            id="recent-queries"
            label="Recent Queries"
            icon={<History size={12} aria-hidden="true" />}
            items={EXAMPLE_QUERIES.map((query) => ({ id: query, label: query }))}
            onSelect={(item) => onSend(item.label)}
            disabled={isStreaming}
          />
          <QuickPromptMenu
            id="job-descriptions"
            label="Job Descriptions"
            icon={<Lightbulb size={12} aria-hidden="true" />}
            items={JOB_DESCRIPTIONS.map((jd) => ({ id: jd, label: jd }))}
            onSelect={(item) => {
              setValue(item.label);
              inputRef.current?.focus();
            }}
            disabled={isStreaming}
          />
          <button
            type="button"
            className="pill"
            onClick={() => onSend('What skills do all indexed candidates share?')}
            disabled={isStreaming}
          >
            <span>Example Skills</span>
            <ChevronDown size={12} aria-hidden="true" />
          </button>
        </div>

        <span className={cn('char-counter', tooLong && 'char-counter--over')}>
          {trimmedLength} / {SCREENING_MAX_PROMPT_LENGTH}
        </span>
      </div>

      {tooLong ? (
        <p className="composer-error" role="alert">
          <XCircle size={13} aria-hidden="true" />
          Prompt is {trimmedLength} characters — the server rejects anything over {SCREENING_MAX_PROMPT_LENGTH}.
        </p>
      ) : null}
    </div>
  );
}
