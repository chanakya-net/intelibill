import { createActionGroup, emptyProps, props } from '@ngrx/store';

export interface RegisterRequestedPayload {
  readonly email: string;
  readonly claim?: boolean;
}

export const RegisterActions = createActionGroup({
  source: 'Register',
  events: {
    Requested: props<RegisterRequestedPayload>(),
    Succeeded: emptyProps(),
    Failed: props<{ errorMessage: string }>(),
    'Clear Error': emptyProps(),
    Reset: emptyProps(),
  },
});
