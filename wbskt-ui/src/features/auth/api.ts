import axios from '../../lib/axios';

// Placeholder types - these should be imported from a shared types directory
interface LoginCredentials { email: string; password: string; }
interface RegisterData { name: string; email: string; password: string; }

export const loginUser = async (credentials: LoginCredentials) => {
  const response = await axios.post('/api/users/login', credentials);
  return response.data;
};

export const registerUser = async (data: RegisterData) => {
  const response = await axios.post('/api/users/register', data);
  return response.data;
};
