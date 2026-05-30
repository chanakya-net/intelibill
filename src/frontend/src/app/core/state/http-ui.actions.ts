import { createActionGroup, emptyProps } from '@ngrx/store';

export const HttpUiActions = createActionGroup({
  source: 'HTTP UI',
  events: {
    'Request Started': emptyProps(),
    'Request Ended': emptyProps(),
  },
});
