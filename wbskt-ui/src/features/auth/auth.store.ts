import { create } from 'zustand';
import { persist } from 'zustand/middleware';

// Define the shape of the user profile
interface UserProfile {
  id: number;
  name: string;
  email: string;
}

// Define the state and actions for the store
interface AuthState {
  isAuthenticated: boolean;
  token: string | null;
  user: UserProfile | null;
  setToken: (token: string) => void;
  setUser: (user: UserProfile) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      isAuthenticated: false,
      token: null,
      user: null,
      setToken: (token) => set({ token, isAuthenticated: true }),
      setUser: (user) => set({ user }),
      logout: () => set({ token: null, user: null, isAuthenticated: false }),
    }),
    {
      name: 'auth-storage', // name of the item in the storage (must be unique)
      onRehydrateStorage: (state) => {
        // This function is called when the state is rehydrated from storage.
        if (state.token) {
          state.isAuthenticated = true;
        }
      },
    }
  )
);
