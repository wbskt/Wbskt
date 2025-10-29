import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  isAuthenticated = signal<boolean>(false);

  constructor() {
    // Check for a token on startup
    const token = localStorage.getItem('wbskt_auth_token');
    this.isAuthenticated.set(!!token);
  }

  login(email: string, password: string): Promise<void> {
    // Mock API call
    return new Promise(resolve => {
      setTimeout(() => {
        const dummyToken = `dummy-token-for-${email}`;
        localStorage.setItem('wbskt_auth_token', dummyToken);
        this.isAuthenticated.set(true);
        resolve();
      }, 500);
    });
  }

  logout(): void {
    localStorage.removeItem('wbskt_auth_token');
    this.isAuthenticated.set(false);
  }
}