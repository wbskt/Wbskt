import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { StudioPageComponent } from './pages/studio-page/studio-page';

const routes: Routes = [
  {
    path: '',
    component: StudioPageComponent
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class StudioRoutingModule { }