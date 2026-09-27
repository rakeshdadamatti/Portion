import ReactMarkdown from 'react-markdown';
import { Bot, User } from 'lucide-react';
import { cn } from '../../../lib/cn';
import type { ChatMessage } from '../hooks/useScreeningStream';

export interface ChatBubbleProps {
  message: ChatMessage;
}

/**
 * Assistant messages render markdown (the model emits GFM); user messages are
 * shown as plain text so prompt markup is never interpreted.
 */
export function ChatBubble({ message }: ChatBubbleProps) {
  const isUser = message.role === 'user';
  const isThinking = !isUser && message.pending && message.text.length === 0;

  return (
    <div className={cn('chat-bubble-wrap', isUser && 'chat-bubble-wrap--hr')}>
      <div className="bubble-avatar" aria-hidden="true">
        {isUser ? <User size={15} /> : <Bot size={16} />}
      </div>
      <div className={cn('chat-bubble', isUser ? 'chat-bubble--hr' : 'chat-bubble--copilot')}>
        {isThinking ? (
          <span className="thinking-indicator">
            <span className="thinking-dots" aria-hidden="true">
              <i />
              <i />
              <i />
            </span>
            Thinking &amp; searching the vector store…
          </span>
        ) : isUser ? (
          message.text
        ) : (
          <div className="markdown-body">
            <ReactMarkdown>{message.text}</ReactMarkdown>
          </div>
        )}
      </div>
    </div>
  );
}
