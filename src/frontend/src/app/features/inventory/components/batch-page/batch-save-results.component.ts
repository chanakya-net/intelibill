import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { TableModule } from 'primeng/table';

import { AddInventoryBatchResponse } from '../../services/inventory.service';

@Component({
  selector: 'app-batch-save-results',
  standalone: true,
  imports: [TranslocoPipe, TableModule],
  templateUrl: './batch-save-results.component.html',
})
export class BatchSaveResultsComponent {
  @Input() summary: AddInventoryBatchResponse | null = null;
}
