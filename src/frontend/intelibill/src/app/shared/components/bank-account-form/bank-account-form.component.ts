import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

@Component({
  selector: 'app-bank-account-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './bank-account-form.component.html',
  styleUrl: './bank-account-form.component.scss',
})
export class BankAccountFormComponent {
  @Input({ required: true }) form!: FormGroup;
  @Input() idPrefix = 'bank';

  readonly accountTypeOptions = [
    { labelKey: 'shops.savings', value: 'Savings' },
    { labelKey: 'shops.current', value: 'Current' },
  ];
}
