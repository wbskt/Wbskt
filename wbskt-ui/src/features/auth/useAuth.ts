import { useNavigate } from 'react-router-dom';
import { useAuthStore } from './auth.store';
import { loginUser, registerUser, type LoginCredentials, type RegisterData } from './api';

export const useAuth = () => {
  const {
    isAuthenticated,
    user,
    token,
    setToken,
    setUser,
    logout: storeLogout,
  } = useAuthStore();
  const navigate = useNavigate();

  const login = async (credentials: LoginCredentials) => {
    try {
      const data = await loginUser(credentials);
      setToken(data.accessToken); // Assuming the token is in accessToken
      // You would also set the user profile from the response here
      // setUser(data.user);
      navigate('/dashboard');
    } catch (error) {
      console.error('Login failed', error);
      // You would set an error state here to display in the UI
    }
  };

  const register = async (data: RegisterData) => {
    try {
      await registerUser(data);
      navigate('/login'); // Redirect to login after successful registration
    } catch (error) {
      console.error('Registration failed', error);
    }
  };

  const logout = () => {
    storeLogout();
    navigate('/login');
  };

  return { isAuthenticated, user, token, login, register, logout };
};
