import { useEffect, useRef } from 'react';
import { ChatBubble } from './ChatBubble';
import type { ChatMessage } from '../hooks/useScreeningStream';

export interface ChatTranscriptProps {
  messages: readonly ChatMessage[];
  isStreaming: boolean;
}

export function ChatTranscript({ messages, isStreaming }: ChatTranscriptProps) {
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages]);

  if (messages.length === 0) return null;

  return (
    <div className="chat-messages" role="log" aria-live="polite" aria-busy={isStreaming || undefined}>
      {messages.map((message) => (
        <ChatBubble key={message.id} message={message} />
      ))}
      <div ref={endRef} />
    </div>
  );
}
