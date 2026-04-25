import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { BankAccountFormComponent } from './bank-account-form.component';

describe('BankAccountFormComponent', () => {
  const createForm = () => new FormGroup({
    bankName: new FormControl('', [Validators.required]),
    accountNumber: new FormControl('', [Validators.required]),
    accountType: new FormControl(''),
    ifscCode: new FormControl('', [Validators.pattern(/^[A-Z]{4}0[A-Z0-9]{6}$/)]),
    accountHolderName: new FormControl(''),
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BankAccountFormComponent, ReactiveFormsModule, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });
  });

  it('renders with provided form group', () => {
    const fixture = TestBed.createComponent(BankAccountFormComponent);
    const component = fixture.componentInstance;
    component.form = createForm();
    fixture.detectChanges();

    const formEl = fixture.nativeElement.querySelector('div.bank-account-form');
    expect(formEl).toBeTruthy();
  });

  it('validates ifsc pattern', () => {
    const fixture = TestBed.createComponent(BankAccountFormComponent);
    const component = fixture.componentInstance;
    const form = createForm();
    component.form = form;
    fixture.detectChanges();

    const ifscCtrl = form.get('ifscCode');
    
    ifscCtrl?.setValue('INVALID');
    expect(ifscCtrl?.invalid).toBe(true);

    ifscCtrl?.setValue('SBIN0001234');
    expect(ifscCtrl?.valid).toBe(true);
  });
});
