import { CommonModule, DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import type { InventoryBatchDto } from '../../services/inventory.models';

@Component({
  selector: 'app-batches-table',
  standalone: true,
  imports: [CommonModule, DecimalPipe, AvatarModule, ButtonModule, CardModule, TagModule, TableModule, TranslocoPipe],
  templateUrl: './batches-table.component.html',
  styleUrl: './batches-table.component.scss',
})
export class BatchesTableComponent {
  @Input({ required: true }) batches: InventoryBatchDto[] = [];
  @Input() loading = false;
  @Output() batchClicked = new EventEmitter<string>();

  onRowAction(action: string, batchId: string): void {
    this.batchClicked.emit(`${action}:${batchId}`);
  }

  onRowSelect(batchId: string | null | undefined): void {
    if (!batchId) {
      return;
    }

    this.batchClicked.emit(batchId);
  }

  productInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].slice(0, 2).toUpperCase();

    return (words[0][0] + words[1][0]).toUpperCase();
  }

  productAvatarColor(name: string): string {
    const colors = [
      '#b45309',
      '#0369a1',
      '#15803d',
      '#7c3aed',
      '#be185d',
      '#c2410c',
      '#0f766e',
      '#1d4ed8',
    ];

    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }

    return colors[Math.abs(hash) % colors.length];
  }
}
