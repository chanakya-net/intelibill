export interface AppShellState {
  readonly sidebarCollapsed: boolean;
}

export interface RootState {
  readonly appShell: AppShellState;
}
