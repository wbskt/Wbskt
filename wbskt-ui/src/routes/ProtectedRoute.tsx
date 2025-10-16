import { Navigate, Outlet } from 'react-router-dom';

// In a real implementation, this would use a proper auth hook
const useAuth = () => {
  // Placeholder logic
  const token = localStorage.getItem('auth_token');
  return { isAuthenticated: !!token };
};

export const ProtectedRoute = () => {
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
};
