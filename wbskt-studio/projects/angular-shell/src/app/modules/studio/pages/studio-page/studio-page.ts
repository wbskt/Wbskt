import { Component, OnInit, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiClientService } from '../../../../core/api/api-client';

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
  workflowData: any = null; // Will hold the fetched workflow data

  constructor(
    private route: ActivatedRoute,
    private apiClient: ApiClientService
  ) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.workflowId = params.get('workflowId');
      if (this.workflowId) {
        // TODO: Fetch actual workflow data using apiClient
        // For now, simulate fetching
        console.log(`Fetching workflow: ${this.workflowId}`);
        this.workflowData = { id: this.workflowId, name: `Workflow ${this.workflowId}`, nodes: [], edges: [] };
      }
    });
  }
}