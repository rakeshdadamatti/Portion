import { Navigate, createBrowserRouter } from 'react-router-dom';
import { App } from './App';
import { ScreeningPage } from '../features/screening/ScreeningPage';
import { ResumeRegistryPage } from '../features/resumes/ResumeRegistryPage';
import { UploadResumePage } from '../features/upload/UploadResumePage';
import { SyncRepositoryPage } from '../features/sync/SyncRepositoryPage';

export const ROUTE_PATHS = {
  screening: '/screening',
  resumes: '/resumes',
  upload: '/upload',
  sync: '/sync',
} as const;

/**
 * `App` is the persistent layout (header + nav + toast host); the feature pages
 * are children rendered through its `<Outlet />`.
 */
export const router = createBrowserRouter([
  {
    element: <App />,
    children: [
      { index: true, element: <Navigate to={ROUTE_PATHS.screening} replace /> },
      { path: 'screening', element: <ScreeningPage /> },
      { path: 'resumes', element: <ResumeRegistryPage /> },
      { path: 'upload', element: <UploadResumePage /> },
      { path: 'sync', element: <SyncRepositoryPage /> },
      { path: '*', element: <Navigate to={ROUTE_PATHS.screening} replace /> },
    ],
  },
]);
