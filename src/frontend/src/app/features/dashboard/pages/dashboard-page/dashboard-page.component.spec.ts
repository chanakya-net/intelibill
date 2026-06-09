import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  it('renders no visible dashboard content', () => {
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });
});
