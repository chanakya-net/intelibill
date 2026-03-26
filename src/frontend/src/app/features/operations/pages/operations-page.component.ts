import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';

@Component({
  selector: 'app-operations-page',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule, TagModule],
  templateUrl: './operations-page.component.html',
})
export class OperationsPageComponent {
  readonly queues = [
    { lane: 'Inbound Validation', owner: 'Warehouse A', pending: 12, severity: 'warn' as const },
    { lane: 'Cycle Count Reconciliation', owner: 'Warehouse B', pending: 6, severity: 'success' as const },
    { lane: 'Vendor Returns', owner: 'Central Hub', pending: 9, severity: 'info' as const },
  ];
}
