import { Component, OnInit, CUSTOM_ELEMENTS_SCHEMA, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiClientService } from '../../../../core/api/api-client';
import { Workflow } from '../../../../core/api/types';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-studio-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './studio-page.html',
  styleUrls: ['./studio-page.scss'],
  schemas: [CUSTOM_ELEMENTS_SCHEMA] // Allow custom elements like <wbskt-react-flow>
})
export class StudioPageComponent implements OnInit {
  workflowId: string | null = null;
  workflowData$!: Observable<Workflow>; // Will hold the fetched workflow data

  constructor(
    private route: ActivatedRoute,
    private apiClient: ApiClientService
  ) { }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.workflowId = params.get('workflowId');
      if (this.workflowId) {
        this.workflowData$ = this.apiClient.getWorkflow(this.workflowId);
      }
    });
  }

  @HostListener('window:workflowUpdated', ['$event as CustomEvent'])
  onWorkflowUpdated(event: Event) {
    const customEvent = event as CustomEvent;
    const { workflowId, workflowData } = customEvent.detail;
    console.log('Workflow updated from React:', workflowId, workflowData);
    // TODO: Call apiClient.saveWorkflow(workflowId, workflowData);
  }
}