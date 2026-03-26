import { createActionGroup, emptyProps, props } from '@ngrx/store';

export interface RegisterRequestedPayload {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly password: string;
  readonly rememberMe: boolean;
}

export const RegisterActions = createActionGroup({
  source: 'Register',
  events: {
    Requested: props<RegisterRequestedPayload>(),
    Succeeded: emptyProps(),
    Failed: props<{ errorMessage: string }>(),
    'Clear Error': emptyProps(),
  },
});
