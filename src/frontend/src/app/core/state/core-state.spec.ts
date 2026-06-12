import { AppShellActions } from './app-shell.actions';
import { appShellReducer } from './app-shell.reducer';
import { HttpUiActions } from './http-ui.actions';
import { httpUiReducer } from './http-ui.reducer';

describe('appShellReducer', () => {
  const initial = appShellReducer(undefined, { type: '@@INIT' } as never);

  it('initial state has sidebarCollapsed=false and sidebarPinned=false', () => {
    expect(initial.sidebarCollapsed).toBe(false);
    expect(initial.sidebarPinned).toBe(false);
    expect(initial.currentLanguage).toBe('en-IN');
  });

  it('toggles sidebarCollapsed on toggleSidebar', () => {
    const next = appShellReducer(initial, AppShellActions.toggleSidebar());
    expect(next.sidebarCollapsed).toBe(true);
    const toggled = appShellReducer(next, AppShellActions.toggleSidebar());
    expect(toggled.sidebarCollapsed).toBe(false);
  });

  it('sets sidebarCollapsed on setSidebarCollapsed', () => {
    const next = appShellReducer(initial, AppShellActions.setSidebarCollapsed({ collapsed: true }));
    expect(next.sidebarCollapsed).toBe(true);
  });

  it('toggles sidebarPinned on toggleSidebarPinned', () => {
    const next = appShellReducer(initial, AppShellActions.toggleSidebarPinned());
    expect(next.sidebarPinned).toBe(true);
    const toggled = appShellReducer(next, AppShellActions.toggleSidebarPinned());
    expect(toggled.sidebarPinned).toBe(false);
  });

  it('sets sidebarPinned on setSidebarPinned', () => {
    const next = appShellReducer(initial, AppShellActions.setSidebarPinned({ pinned: true }));
    expect(next.sidebarPinned).toBe(true);
  });

  it('sets language on setLanguage', () => {
    const next = appShellReducer(initial, AppShellActions.setLanguage({ language: 'hi-IN' }));
    expect(next.currentLanguage).toBe('hi-IN');
  });
});

describe('appShellSelectors', () => {
  it('selectCurrentLanguage returns currentLanguage', async () => {
    const { selectCurrentLanguage } = await import('./app-shell.selectors');
    const state = { appShell: { sidebarCollapsed: false, sidebarPinned: false, currentLanguage: 'ta-IN' } };
    expect(selectCurrentLanguage(state as never)).toBe('ta-IN');
  });
});

describe('httpUiReducer', () => {
  const initial = httpUiReducer(undefined, { type: '@@INIT' } as never);

  it('initial state has 0 pendingRequests', () => {
    expect(initial.pendingRequests).toBe(0);
  });

  it('increments pendingRequests on requestStarted', () => {
    const next = httpUiReducer(initial, HttpUiActions.requestStarted());
    expect(next.pendingRequests).toBe(1);
  });

  it('decrements pendingRequests on requestEnded', () => {
    const withPending = httpUiReducer(initial, HttpUiActions.requestStarted());
    const next = httpUiReducer(withPending, HttpUiActions.requestEnded());
    expect(next.pendingRequests).toBe(0);
  });

  it('clamps pendingRequests to 0 on requestEnded when already 0', () => {
    const next = httpUiReducer(initial, HttpUiActions.requestEnded());
    expect(next.pendingRequests).toBe(0);
  });
});
