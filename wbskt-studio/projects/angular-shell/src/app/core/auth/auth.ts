import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { ApiClientService } from '../api/api-client';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  isAuthenticated = signal<boolean>(false);

  constructor(private apiClient: ApiClientService, private router: Router) {
    const token = localStorage.getItem('wbskt_auth_token');
    this.isAuthenticated.set(!!token);
  }

  login(credentials: any) {
    return this.apiClient.login(credentials).pipe(
      tap(response => {
        localStorage.setItem('wbskt_auth_token', response.token);
        this.isAuthenticated.set(true);
      })
    );
  }

  logout(): void {
    localStorage.removeItem('wbskt_auth_token');
    this.isAuthenticated.set(false);
    this.router.navigate(['/login']);
  }
}