import { useCallback, useEffect, useMemo } from 'react';
import { RotateCcw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { useToast } from '../../app/providers/ToastProvider';
import { useAsync } from '../../hooks/useAsync';
import { listResumes, MAX_PAGE_SIZE } from '../../services/resumeApi';
import { toUserMessage } from '../../services/httpClient';
import type { ResumeSummary } from '../../types/api';
import { WelcomeBanner } from './components/WelcomeBanner';
import { MatchPanel } from './components/MatchPanel';
import { ChatTranscript } from './components/ChatTranscript';
import { PipelineStatus } from './components/PipelineStatus';
import { PromptComposer } from './components/PromptComposer';
import { useScreeningStream } from './hooks/useScreeningStream';

export function ScreeningPage() {
  const toast = useToast();
  const { messages, isStreaming, stage, matches, elapsedMs, send, cancel, reset } = useScreeningStream();

  // Registry lookup used *only* to enrich match cards with real status /
  // chunk counts / last-synced values. A failure here degrades the cards, it
  // never blocks the screening flow.
  const loadRegistryLookup = useCallback(async (): Promise<Map<string, ResumeSummary>> => {
    const result = await listResumes({ page: 1, pageSize: MAX_PAGE_SIZE });
    return new Map(result.items.map((item) => [item.id, item]));
  }, []);

  const {
    run: runRegistryLookup,
    data: resumesById,
    error: registryError,
  } = useAsync(loadRegistryLookup, {
    onError: (error) => toast.error(toUserMessage(error, 'Could not load the resume registry.')),
  });

  useEffect(() => {
    void runRegistryLookup();
  }, [runRegistryLookup]);

  const resumeMap = useMemo(() => resumesById ?? new Map<string, ResumeSummary>(), [resumesById]);

  return (
    <div className="chat-layout">
      <WelcomeBanner />
      <MatchPanel matches={matches} resumesById={resumeMap} />

      {registryError !== null ? (
        <p className="inline-error" role="alert">
          {toUserMessage(registryError, 'Resume registry lookup failed — match cards show score only.')}
        </p>
      ) : null}

      <ChatTranscript messages={messages} isStreaming={isStreaming} />
      <PipelineStatus stage={stage} isStreaming={isStreaming} elapsedMs={elapsedMs} />

      {messages.length > 0 ? (
        <div className="chat-reset">
          <Button variant="ghost" size="sm" icon={<RotateCcw size={14} aria-hidden="true" />} onClick={reset} disabled={isStreaming}>
            New conversation
          </Button>
        </div>
      ) : null}

      <PromptComposer isStreaming={isStreaming} onSend={send} onCancel={cancel} />
    </div>
  );
}
