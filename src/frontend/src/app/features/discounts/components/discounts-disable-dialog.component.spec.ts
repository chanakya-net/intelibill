import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { DiscountsDisableDialogComponent } from './discounts-disable-dialog.component';

describe('DiscountsDisableDialogComponent', () => {
  let fixture: ComponentFixture<DiscountsDisableDialogComponent>;
  let component: DiscountsDisableDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DiscountsDisableDialogComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(DiscountsDisableDialogComponent);
    component = fixture.componentInstance;
    component.visible = false;
  });

  it('initializes with provided inputs', () => {
    fixture.detectChanges();
    expect(component.visible).toBe(false);
  });
});
