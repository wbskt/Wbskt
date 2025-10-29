import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { WorkflowsRoutingModule } from './workflows-routing-module';


import { ComponentsModule } from '../../shared/components/components-module';

@NgModule({
  declarations: [],
  imports: [
    CommonModule,
    WorkflowsRoutingModule,
    ComponentsModule
  ]
})
export class WorkflowsModule { }
