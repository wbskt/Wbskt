import axios from 'axios';
import { useAuthStore } from '../features/auth/auth.store';

const apiClient = axios.create({
  baseURL: 'http://localhost:5070', // Replace with your actual backend URL
});

// Request Interceptor: Attach the JWT to every request
apiClient.interceptors.request.use(
  (config) => {
    const token = useAuthStore.getState().token;
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response Interceptor: Handle 401 Unauthorized errors (e.g., expired token)
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response && error.response.status === 401) {
      // Call the logout function from your auth store
      useAuthStore.getState().logout();
      // Optionally, redirect to the login page
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export default apiClient;
