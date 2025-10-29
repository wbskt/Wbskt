import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DashboardStats, Workflow } from './types';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ApiClientService {
  private readonly baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getDashboardStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.baseUrl}/dashboard/stats`);
  }

  getWorkflows(): Observable<Workflow[]> {
    return this.http.get<Workflow[]>(`${this.baseUrl}/workflows`);
  }

  login(credentials: any): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.baseUrl}/users/login`, credentials);
  }
}