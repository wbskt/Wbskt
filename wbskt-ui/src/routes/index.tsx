import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from '../components/layout/MainLayout';
import { ProtectedRoute } from './ProtectedRoute';
import { LoginPage } from '../features/auth/LoginPage';
import { RegisterPage } from '../features/auth/RegisterPage';

// Placeholders for pages that are not yet built
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { WorkflowsListPage } from '../features/workflows/WorkflowsListPage';
import { ExecutionsListPage } from '../features/executions/ExecutionsListPage';
import { ClientsPage } from '../features/clients/ClientsPage';
import { PoliciesPage } from '../features/policies/PoliciesPage';
import { IntegrationsPage } from '../features/integrations/IntegrationsPage';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    path: '/register',
    element: <RegisterPage />,
  },
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      {
        element: <MainLayout />,
        children: [
          { path: 'dashboard', element: <DashboardPage /> },
          { path: 'workflows', element: <WorkflowsListPage /> },
          { path: 'executions', element: <ExecutionsListPage /> },
          { path: 'clients', element: <ClientsPage /> },
          { path: 'policies', element: <PoliciesPage /> },
          { path: 'integrations', element: <IntegrationsPage /> },
          { path: '/', element: <Navigate to="/dashboard" replace /> },
        ],
      },
    ],
  },
]);
