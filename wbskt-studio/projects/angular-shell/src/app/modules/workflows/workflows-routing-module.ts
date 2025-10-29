import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { WorkflowsListPageComponent } from './pages/workflows-list-page/workflows-list-page';

const routes: Routes = [
  {
    path: '',
    component: WorkflowsListPageComponent
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class WorkflowsRoutingModule { }