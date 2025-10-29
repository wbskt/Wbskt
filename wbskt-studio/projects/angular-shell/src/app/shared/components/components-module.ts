import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonComponent } from './button/button';
import { CardComponent } from './card/card';
import { IconComponent } from './icon/icon';

@NgModule({
  declarations: [],
  imports: [
    CommonModule,
    ButtonComponent,
    CardComponent,
    IconComponent
  ],
  exports: [
    ButtonComponent,
    CardComponent,
    IconComponent
  ]
})
export class ComponentsModule { }