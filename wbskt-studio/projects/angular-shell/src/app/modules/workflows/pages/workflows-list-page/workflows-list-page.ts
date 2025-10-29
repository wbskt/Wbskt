import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonComponent } from '../../../../shared/components/button/button';
import { ApiClientService } from '../../../../core/api/api-client';
import { Workflow } from '../../../../core/api/types';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-workflows-list-page',
  standalone: true,
  imports: [CommonModule, ButtonComponent],
  templateUrl: './workflows-list-page.html',
  styleUrls: ['./workflows-list-page.scss']
})
export class WorkflowsListPageComponent implements OnInit {
  workflows$!: Observable<Workflow[]>;

  constructor(private apiClient: ApiClientService) {}

  ngOnInit(): void {
    this.workflows$ = this.apiClient.getWorkflows();
  }
}