import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { DiscountsDisableDialogComponent } from './discounts-disable-dialog.component';

describe('DiscountsDisableDialogComponent', () => {
  let fixture: ComponentFixture<DiscountsDisableDialogComponent>;
  let component: DiscountsDisableDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        DiscountsDisableDialogComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DiscountsDisableDialogComponent);
    component = fixture.componentInstance;
    component.visible = false;
  });

  it('initializes with provided inputs', () => {
    fixture.detectChanges();
    expect(component.visible).toBe(false);
  });

  it('renders API feedback inside the dialog', () => {
    component.visible = true;
    component.errorKey = 'discounts.errors.disableFailed';
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('forwards hide attempts when dismissal is unlocked', () => {
    let closes = 0;
    component.closeRequested.subscribe(() => (closes += 1));

    component.onDialogHide();
    expect(closes).toBe(1);

    component.disableSubmitting = true;
    component.onDialogHide();
    expect(closes).toBe(1);
  });
});
