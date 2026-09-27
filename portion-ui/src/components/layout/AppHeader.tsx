import { Moon, Sparkles, Sun } from 'lucide-react';
import { useTheme } from '../../app/providers/ThemeProvider';
import { AppNav } from './AppNav';

export function AppHeader() {
  const { theme, toggleTheme } = useTheme();
  const nextTheme = theme === 'dark' ? 'light' : 'dark';

  return (
    <header className="app-header">
      <div className="header-brand">
        <div className="brand-icon" aria-hidden="true">
          <Sparkles size={22} color="#38bdf8" />
        </div>
        <div>
          <div className="brand-title">Portion AI</div>
          <div className="brand-sub">Resume Search &amp; Interactive Screening Engine</div>
        </div>
      </div>

      <AppNav />

      <button
        type="button"
        className="theme-toggle"
        onClick={toggleTheme}
        title={`Switch to ${nextTheme} theme`}
        aria-label={`Switch to ${nextTheme} theme`}
        aria-pressed={theme === 'dark'}
      >
        <Sun size={16} className="icon-sun" aria-hidden="true" />
        <Moon size={16} className="icon-moon" aria-hidden="true" />
      </button>
    </header>
  );
}
