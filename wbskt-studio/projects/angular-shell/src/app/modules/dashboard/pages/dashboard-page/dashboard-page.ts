import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent } from '../../../../shared/components/card/card';
import { ApiClientService } from '../../../../core/api/api-client';
import { DashboardStats } from '../../../../core/api/types';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, CardComponent],
  templateUrl: './dashboard-page.html',
  styleUrls: ['./dashboard-page.scss']
})
export class DashboardPageComponent implements OnInit {
  dashboardStats$!: Observable<DashboardStats>;

  constructor(private apiClient: ApiClientService) {}

  ngOnInit(): void {
    this.dashboardStats$ = this.apiClient.getDashboardStats();
  }
}
