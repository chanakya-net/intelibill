import { RegisterState } from '../../features/auth/state/register.reducer';
export type { RegisterState } from '../../features/auth/state/register.reducer';
export interface AppShellState {
  readonly sidebarCollapsed: boolean;
  readonly sidebarPinned: boolean;
  readonly currentLanguage: string;
}

export interface HttpUiState {
  readonly pendingRequests: number;
}

export interface RootState {
  readonly appShell: AppShellState;
  readonly httpUi: HttpUiState;
  readonly authRegistration: RegisterState;
}
