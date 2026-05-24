import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { BatchRowFormStateService } from '../../services/batch-row-form-state.service';

@Component({
  selector: 'app-batch-hsn-picker-dialog',
  standalone: true,
  imports: [FormsModule, TranslocoPipe, AutoCompleteModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './batch-hsn-picker-dialog.component.html',
})
export class BatchHsnPickerDialogComponent {
  readonly state = inject(BatchRowFormStateService);
}
