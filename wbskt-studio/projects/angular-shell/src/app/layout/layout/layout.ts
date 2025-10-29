import { Component } from '@angular/core';
import { RouterOutlet } from "@angular/router";
import { SidebarComponent } from "../sidebar/sidebar";
import { Topbar } from "../topbar/topbar";

@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, SidebarComponent, Topbar],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
})
export class LayoutComponent {

}
