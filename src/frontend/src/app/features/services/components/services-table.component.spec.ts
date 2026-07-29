import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ServicesTableComponent } from './services-table.component';

const enIN = JSON.parse(
  readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8'),
) as Record<string, unknown>;

describe('ServicesTableComponent', () => {
  it('lays out as a full-width block so the pagination label does not collapse', () => {
    const fixture = TestBed.configureTestingModule({
      imports: [
        ServicesTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
          preloadLangs: true,
        }),
      ],
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
