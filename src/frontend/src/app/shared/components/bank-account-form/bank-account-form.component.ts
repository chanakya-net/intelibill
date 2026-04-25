import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

@Component({
  selector: 'app-bank-account-form',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputTextModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './bank-account-form.component.html',
  styleUrl: './bank-account-form.component.scss',
})
export class BankAccountFormComponent {
  private readonly transloco = inject(TranslocoService);

  @Input({ required: true }) form!: FormGroup;
  @Input() idPrefix = 'bank';

  readonly accountTypeOptions = [
    { label: 'Savings', value: 'Savings' },
    { label: 'Current', value: 'Current' },
  ];
}
