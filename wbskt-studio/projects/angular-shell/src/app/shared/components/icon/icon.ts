import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  templateUrl: './icon.html',
  styleUrls: ['./icon.scss']
})
export class IconComponent {
  @Input() name = '';
  @Input() size = 24;
  @Input() strokeWidth = 1.5;
}