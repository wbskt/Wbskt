import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorkflowsListPage } from './workflows-list-page';

describe('WorkflowsListPage', () => {
  let component: WorkflowsListPage;
  let fixture: ComponentFixture<WorkflowsListPage>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorkflowsListPage]
    })
    .compileComponents();

    fixture = TestBed.createComponent(WorkflowsListPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
