import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { ServicesTableComponent } from './services-table.component';

describe('ServicesTableComponent', () => {
  it('lays out as a full-width block so the pagination label does not collapse', () => {
    const fixture = TestBed.configureTestingModule({
      imports: [ServicesTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).createComponent(ServicesTableComponent);

    fixture.componentRef.setInput('services', []);
    fixture.componentRef.setInput('totalCount', 1);
    fixture.componentRef.setInput('footerStart', 1);
    fixture.componentRef.setInput('footerEnd', 1);
    fixture.detectChanges();

    const caption = fixture.nativeElement.querySelector('.table-caption') as HTMLElement | null;

    expect(getComputedStyle(fixture.nativeElement).display).toBe('block');
    expect(caption?.textContent?.trim()).toBe('1 - 1 of 1');
    expect(caption ? getComputedStyle(caption).whiteSpace : '').toBe('nowrap');
  });
});
