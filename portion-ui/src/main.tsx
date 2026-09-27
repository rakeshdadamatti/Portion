import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { router } from './app/router';
import { ThemeProvider } from './app/providers/ThemeProvider';
import { ToastProvider } from './app/providers/ToastProvider';
import { ErrorBoundary } from './components/ErrorBoundary';
import './styles/index.css';

const container = document.getElementById('root');
if (container === null) throw new Error('Root container #root is missing from index.html');

createRoot(container).render(
  <StrictMode>
    <ErrorBoundary fallbackTitle="Portion AI could not start">
      <ThemeProvider>
        <ToastProvider>
          <RouterProvider router={router} />
        </ToastProvider>
      </ThemeProvider>
    </ErrorBoundary>
  </StrictMode>,
);
