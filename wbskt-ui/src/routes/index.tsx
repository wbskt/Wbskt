import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from '../components/layout/MainLayout';
import { ProtectedRoute } from './ProtectedRoute';

// Placeholder Pages
const LoginPage = () => <h1>Login Page</h1>;
const RegisterPage = () => <h1>Register Page</h1>;
const DashboardPage = () => <h1>Dashboard</h1>;
const WorkflowsListPage = () => <h1>Workflows</h1>;
const ExecutionsListPage = () => <h1>Executions</h1>;
const ClientsPage = () => <h1>Clients</h1>;
const PoliciesPage = () => <h1>Policies</h1>;
const IntegrationsPage = () => <h1>Integrations</h1>;

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
