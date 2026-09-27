import { Bot } from 'lucide-react';

export function WelcomeBanner() {
  return (
    <section className="welcome-banner">
      <Bot size={32} className="welcome-icon" aria-hidden="true" />
      <div>
        <p>
          Hello! I am <strong>Portion AI</strong>, your private candidate screening copilot. Ask me to find skills, compare
          candidate experience, or screen candidates based on job descriptions.
        </p>
        <p className="welcome-banner__meta">
          Answers are streamed live from the screening pipeline. Every match score shown comes from the vector search —
          nothing is estimated.
        </p>
      </div>
    </section>
  );
}
