export interface AppShellState {
  readonly sidebarCollapsed: boolean;
}

export interface HttpUiState {
  readonly pendingRequests: number;
}

export interface RegisterState {
  readonly submitting: boolean;
  readonly errorMessage: string;
}

export interface RootState {
  readonly appShell: AppShellState;
  readonly httpUi: HttpUiState;
  readonly authRegistration: RegisterState;
}
