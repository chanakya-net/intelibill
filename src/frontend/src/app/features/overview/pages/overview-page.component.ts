import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';

@Component({
  selector: 'app-overview-page',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule, TagModule],
  templateUrl: './overview-page.component.html',
})
export class OverviewPageComponent {
  readonly summary = [
    { label: 'Open Orders', value: '142', trend: '+8.4%' },
    { label: 'Stock Accuracy', value: '98.1%', trend: '+0.6%' },
    { label: 'Fulfillment SLA', value: '96.8%', trend: '+1.2%' },
  ];
}
