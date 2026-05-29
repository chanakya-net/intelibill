import { Component, EventEmitter, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';

import { InventoryService } from '../../inventory/services/inventory.service';
import { ServiceService } from '../services/service.service';

@Component({
  selector: 'app-add-service-overlay',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, CheckboxModule, InputTextModule, TextareaModule, TranslocoPipe],
  templateUrl: './add-service-overlay.component.html',
  styleUrl: './add-service-overlay.component.scss',
})
export class AddServiceOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly serviceService = inject(ServiceService);
  private readonly inventoryService = inject(InventoryService);

  readonly isSubmitting = signal(false);
  readonly isLoadingHsn = signal(false);
  readonly serverError = signal('');
  readonly suggestedHsnCodes = signal<string[]>([]);
  readonly suggestedTaxSlabs = signal<string[]>([]);

  @Output() readonly closeRequested = new EventEmitter<void>();
  @Output() readonly saved = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    description: ['', [Validators.maxLength(320)]],
    price: [0, [Validators.required, Validators.min(0)]],
    taxIncluded: [true, [Validators.required]],
    taxRatePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
    hsnCode: ['', [Validators.pattern(/^\s*\d{4,8}\s*$/)]],
  });

  ngOnInit(): void {
    this.serverError.set('');
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onNameBlur(): void {
    void this.lookupHsn();
  }

  selectSuggestedHsnCode(hsnCode: string): void {
    this.form.controls.hsnCode.setValue(hsnCode);
  }

  selectSuggestedTaxSlab(taxPercentage: string): void {
    const parsedTaxRate = this.parseTaxPercentage(taxPercentage);
    if (parsedTaxRate !== null) {
      this.form.controls.taxRatePercent.setValue(parsedTaxRate);
    }
  }

  async onSubmit(): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.serverError.set('');

    this.serviceService
      .addService({
        name: this.form.controls.name.value.trim(),
        description: this.nullableTrimmed(this.form.controls.description.value),
        price: Number(this.form.controls.price.value),
        hsnCode: this.nullableTrimmed(this.form.controls.hsnCode.value),
        taxRatePercent: Number(this.form.controls.taxRatePercent.value),
        taxIncluded: this.form.controls.taxIncluded.value,
        isActive: true,
      })
      .subscribe({
        next: () => {
          this.saved.emit();
          this.closeRequested.emit();
          this.isSubmitting.set(false);
        },
        error: () => {
          this.serverError.set('services.saveFailed');
          this.isSubmitting.set(false);
        },
      });
  }

  private async lookupHsn(): Promise<void> {
    const productName = this.form.controls.name.value.trim();
    if (productName.length < 3) {
      this.suggestedHsnCodes.set([]);
      this.suggestedTaxSlabs.set([]);
      return;
    }

    this.isLoadingHsn.set(true);
    try {
      const result = await firstValueFrom(this.inventoryService.lookupHsn(productName));
      this.suggestedHsnCodes.set([...result.hsnCodes]);
      this.suggestedTaxSlabs.set(result.taxScenarios.map((scenario) => scenario.taxPercentage));

      const suggestedHsn = result.hsnCodes.length === 1 ? result.hsnCodes[0] : null;
      const suggestedTaxRate =
        result.taxScenarios.length === 1 ? this.parseTaxPercentage(result.taxScenarios[0].taxPercentage) : null;

      if (!this.form.controls.hsnCode.dirty && suggestedHsn) {
        this.form.controls.hsnCode.setValue(suggestedHsn);
      }

      if (!this.form.controls.taxRatePercent.dirty && suggestedTaxRate !== null) {
        this.form.controls.taxRatePercent.setValue(suggestedTaxRate);
      }
    } catch {
      this.suggestedHsnCodes.set([]);
      this.suggestedTaxSlabs.set([]);
    } finally {
      this.isLoadingHsn.set(false);
    }
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private parseTaxPercentage(value: string): number | null {
    const parsed = Number.parseFloat(value.replace('%', '').trim());
    return Number.isFinite(parsed) ? parsed : null;
  }
}
