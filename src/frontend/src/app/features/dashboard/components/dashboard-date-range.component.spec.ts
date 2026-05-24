import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { DashboardDateRangeComponent, DashboardDateRangeChange, SelectOption } from './dashboard-date-range.component';
import { DashboardPreset } from '../state/dashboard.actions';

const presets: SelectOption<DashboardPreset>[] = [
  { label: 'Today', value: 'today' },
  { label: 'Last 7', value: 'last7' },
  { label: 'Custom', value: 'custom' },
];

describe('DashboardDateRangeComponent', () => {
  let fixture: ComponentFixture<DashboardDateRangeComponent>;
  let component: DashboardDateRangeComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardDateRangeComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });

    fixture = TestBed.createComponent(DashboardDateRangeComponent);
    component = fixture.componentInstance;
    component.presets = presets;
    component.preset = 'last30';
    component.startDate = '2026-04-20';
    component.endDate = '2026-04-30';
    fixture.detectChanges();
  });

  it('initializes from explicit input range', () => {
    expect(component.pendingPreset).toBe('last30');
    expect(component.pendingStartDate).toBe('2026-04-20');
    expect(component.pendingEndDate).toBe('2026-04-30');
  });

  it('updates preset and date range for quick preset', () => {
    component.onSelectPreset('last7');
    expect(component.pendingPreset).toBe('last7');
    expect(component.pendingStartDate <= component.pendingEndDate).toBeTruthy();
  });

  it('clamps future end date and marks correction', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-10T00:00:00.000Z'));
    const componentSpy = TestBed.createComponent(DashboardDateRangeComponent).componentInstance;
    componentSpy.presets = presets;
    componentSpy.preset = 'today';
    componentSpy.startDate = '2026-05-01';
    componentSpy.endDate = '2026-05-10';
    componentSpy.ngOnInit();
    componentSpy.onEndDateChange('2099-01-01');

    expect(componentSpy.pendingEndDate).toBe('2026-05-10');
    expect(componentSpy.futureCorrected).toBe(true);

    vi.useRealTimers();
  });

  it('emits rangeChange on apply when valid', () => {
    const onRangeChange = vi.fn();
    component.rangeChange.subscribe((payload: DashboardDateRangeChange) => onRangeChange(payload));

    component.pendingPreset = 'custom';
    component.pendingStartDate = '2026-05-01';
    component.pendingEndDate = '2026-05-04';
    component.onApply();

    expect(onRangeChange).toHaveBeenCalledWith({
      startDate: '2026-05-01',
      endDate: '2026-05-04',
      preset: 'custom',
    });
  });

  it('does not emit when range is invalid', () => {
    const onRangeChange = vi.fn();
    component.rangeChange.subscribe((payload: DashboardDateRangeChange) => onRangeChange(payload));

    component.pendingPreset = 'custom';
    component.pendingStartDate = '2026-05-10';
    component.pendingEndDate = '2026-05-01';
    component.onApply();

    expect(onRangeChange).not.toHaveBeenCalled();
  });

  it('shows apply action in template', () => {
    const button = fixture.debugElement.query(By.css('.range-apply-button'));
    expect(button).not.toBeNull();
    expect((button.nativeElement as HTMLButtonElement).disabled).toBe(false);
  });
});
