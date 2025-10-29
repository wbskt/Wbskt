import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent } from '../../../../shared/components/card/card';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, CardComponent],
  templateUrl: './dashboard-page.html',
  styleUrls: ['./dashboard-page.scss']
})
export class DashboardPageComponent {
  stats = [
    { title: 'Total Workflows', value: 42 },
    { title: 'Enabled Workflows', value: 38 },
    { title: 'Executions (24h)', value: 1024 },
    { title: 'Failed (24h)', value: 16 },
  ];
}