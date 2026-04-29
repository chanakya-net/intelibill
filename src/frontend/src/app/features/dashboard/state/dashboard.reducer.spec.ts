import { DashboardActions } from './dashboard.actions';
import { dashboardReducer, DashboardState } from './dashboard.reducer';
import { DashboardDto } from '../services/dashboard.service';

const makeDashboardDto = (): DashboardDto => ({
  generatedAt: '2026-04-29T11:00:00Z',
  startDate: '2026-03-31',
  endDate: '2026-04-29',
  salesCount: 5,
  hasNoSalesActivity: false,
  salesBooked: 500,
  cashCollected: 450,
  profitBeforeTax: 100,
  profitAfterTax: 130,
  expenseRecorded: 50,
  expenseCorrection: 0,
  netExpense: 50,
  creditSalesAmount: 50,
  creditSalesPercentage: 0.1,
  paymentMix: { cash: 450, upi: 0, card: 0, credit: 50 },
  creditShareWarning: false,
  runningLowStockCount: 0,
  criticalStockCount: 0,
  rankedShortageList: [],
  highestDueCustomer: null,
  topFiveDueCustomers: [],
  alerts: [],
  salesTrendSeries: [],
});

describe('dashboardReducer', () => {
  const initialState = dashboardReducer(undefined, { type: '@@INIT' } as never);

  it('has correct initial state', () => {
    expect(initialState.loading).toBe(false);
    expect(initialState.data).toBeNull();
    expect(initialState.errorMessage).toBe('');
    expect(initialState.selectedPreset).toBe('last30');
    expect(initialState.startDate).toBeTruthy();
    expect(initialState.endDate).toBeTruthy();
  });

  it('sets loading true and clears error when load is requested', () => {
    const next = dashboardReducer(
      { ...initialState, errorMessage: 'old error' },
      DashboardActions.loadDashboardRequested()
    );

    expect(next.loading).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets data and clears loading when load succeeds', () => {
    const dashboard = makeDashboardDto();
    const next = dashboardReducer(
      { ...initialState, loading: true },
      DashboardActions.loadDashboardSucceeded({ dashboard })
    );

    expect(next.loading).toBe(false);
    expect(next.data).toEqual(dashboard);
    expect(next.errorMessage).toBe('');
  });

  it('sets error and clears loading when load fails', () => {
    const next = dashboardReducer(
      { ...initialState, loading: true },
      DashboardActions.loadDashboardFailed({ errorMessage: 'Network error' })
    );

    expect(next.loading).toBe(false);
    expect(next.errorMessage).toBe('Network error');
  });

  it('retains stale data when load fails', () => {
    const dashboard = makeDashboardDto();
    const staleState: DashboardState = { ...initialState, data: dashboard };

    const next = dashboardReducer(
      { ...staleState, loading: true },
      DashboardActions.loadDashboardFailed({ errorMessage: 'Timeout' })
    );

    expect(next.data).toEqual(dashboard);
  });

  it('sets loading true when refresh is requested', () => {
    const next = dashboardReducer(
      initialState,
      DashboardActions.refreshDashboard()
    );

    expect(next.loading).toBe(true);
  });

  it('applyRange sets dates, preset, and loading', () => {
    const next = dashboardReducer(
      initialState,
      DashboardActions.applyRange({ startDate: '2026-04-01', endDate: '2026-04-29', preset: 'custom' })
    );

    expect(next.loading).toBe(true);
    expect(next.startDate).toBe('2026-04-01');
    expect(next.endDate).toBe('2026-04-29');
    expect(next.selectedPreset).toBe('custom');
  });

  it('applyRange clears errorMessage', () => {
    const next = dashboardReducer(
      { ...initialState, errorMessage: 'old error' },
      DashboardActions.applyRange({ startDate: '2026-04-01', endDate: '2026-04-29', preset: 'last7' })
    );

    expect(next.errorMessage).toBe('');
  });
});
