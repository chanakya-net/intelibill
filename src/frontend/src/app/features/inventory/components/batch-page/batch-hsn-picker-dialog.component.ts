import {
  Component,
  computed,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';

import { HsnLookupResult } from '../../services/inventory.service';

@Component({
  selector: 'app-batch-hsn-picker-dialog',
  standalone: true,
  imports: [FormsModule, TranslocoPipe, AutoCompleteModule, ButtonModule],
  templateUrl: './batch-hsn-picker-dialog.component.html',
})
export class BatchHsnPickerDialogComponent implements OnChanges {
  @Input() hsnResult: HsnLookupResult | null = null;
  @Input() visible = false;

  @Output() hsnSelected = new EventEmitter<{ hsnCode: string; taxRate: string }>();
  @Output() cancel = new EventEmitter<void>();

  pickerHsnCode: string | null = null;
  pickerTaxRate: string | null = null;

  readonly hsnOptions = computed(() => [...(this.hsnResult?.hsnCodes ?? [])]);
  readonly taxOptions = computed(() => (this.hsnResult?.taxScenarios ?? []).map((s) => s.taxPercentage));
  readonly filteredHsnOptions = signal<string[]>([]);
  readonly filteredTaxOptions = signal<string[]>([]);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['hsnResult'] && this.hsnResult) {
      this.pickerHsnCode = this.hsnResult.hsnCodes[0] ?? null;
      this.pickerTaxRate = this.hsnResult.taxScenarios[0]?.taxPercentage ?? null;
      this.filteredHsnOptions.set([...this.hsnResult.hsnCodes]);
      this.filteredTaxOptions.set(this.hsnResult.taxScenarios.map((s) => s.taxPercentage));
    }
  }

  filterHsn(event: AutoCompleteCompleteEvent): void {
    const filter = (event.query ?? '').toLowerCase();
    this.filteredHsnOptions.set(this.hsnOptions().filter((h) => h.toLowerCase().includes(filter)));
  }

  filterTax(event: AutoCompleteCompleteEvent): void {
    const filter = (event.query ?? '').toLowerCase();
    this.filteredTaxOptions.set(this.taxOptions().filter((t) => t.toLowerCase().includes(filter)));
  }

  apply(): void {
    if (this.pickerHsnCode && this.pickerTaxRate) {
      this.hsnSelected.emit({ hsnCode: this.pickerHsnCode, taxRate: this.pickerTaxRate });
    }
  }

  onCancel(): void {
    this.cancel.emit();
  }
}
