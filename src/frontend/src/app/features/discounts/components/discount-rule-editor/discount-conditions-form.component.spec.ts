import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it } from 'vitest';

import {
  DiscountConditions,
  DiscountConditionsFormComponent,
} from './discount-conditions-form.component';

describe('DiscountConditionsFormComponent', () => {
  const makeConditions = (overrides: Partial<DiscountConditions> = {}): DiscountConditions => ({
    ruleType: 'BatchPercentage',
    name: '10% off batch',
    description: null,
    percentage: 10,
    thresholdAmount: null,
    startsAt: '',
    endsAt: '',
    belowCostConfirmed: false,
    belowCostConfirmationReason: '',
    disabledReason: '',
    ...overrides,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        DiscountConditionsFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });
  });

  it('emits normalized data on user updates', async () => {
    const fixture = TestBed.createComponent(DiscountConditionsFormComponent);
    const component = fixture.componentInstance;
    const emitted: DiscountConditions[] = [];
    fixture.detectChanges();

    component.conditionsChange.subscribe((value) => emitted.push(value));
    component.initialConditions = makeConditions();
    fixture.detectChanges();

    component.form.controls.name.setValue('  Seasonal   ');
    component.form.controls.description.setValue('  fresh stock  ');

    expect(emitted.at(-1)?.name).toBe('Seasonal');
    expect(emitted.at(-1)?.description).toBe('fresh stock');
  });

  it('requires threshold amount for sale-threshold rules', () => {
    const fixture = TestBed.createComponent(DiscountConditionsFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.controls.ruleType.setValue('SaleThresholdPercentage');
    fixture.detectChanges();
    component.form.controls.thresholdAmount.setValue(null);
    component.form.controls.thresholdAmount.updateValueAndValidity();

    expect(fixture.nativeElement.querySelector('#discount-rule-threshold')).not.toBeNull();
    expect(component.form.controls.thresholdAmount.valid).toBe(false);
    component.form.controls.thresholdAmount.setValue(0.1);
    expect(component.form.controls.thresholdAmount.valid).toBe(true);
  });

  it('marks threshold as valid for non-threshold rules', () => {
    const fixture = TestBed.createComponent(DiscountConditionsFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.controls.ruleType.setValue('BatchPercentage');
    fixture.detectChanges();
    component.form.controls.thresholdAmount.setValue(0.1);
    component.form.controls.thresholdAmount.updateValueAndValidity();

    expect(fixture.nativeElement.querySelector('#discount-rule-threshold')).toBeNull();
    expect(component.form.controls.thresholdAmount.valid).toBe(true);
  });

  it('exposes touched invalid controls to assistive technology', () => {
    const fixture = TestBed.createComponent(DiscountConditionsFormComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    component.form.controls.name.setValue('');
    component.markAllAsTouched();
    fixture.detectChanges();

    const name = fixture.nativeElement.querySelector('#discount-rule-name') as HTMLInputElement;
    expect(name.getAttribute('aria-invalid')).toBe('true');
  });
});
