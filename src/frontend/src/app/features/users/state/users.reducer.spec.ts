import { usersReducer, UsersState } from './users.reducer';
import { UsersActions } from './users.actions';

describe('usersReducer', () => {
  const initialState = usersReducer(undefined, { type: '@@INIT' } as never);

  it('sets submitting state when update profile is requested', () => {
    const next = usersReducer(
      {
        ...initialState,
        errorMessage: 'Existing error',
      },
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'jane@example.com',
          phoneNumber: null,
        },
      })
    );

    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('update-profile');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets success state when update profile succeeds', () => {
    const state: UsersState = {
      submitting: true,
      errorMessage: 'Old error',
      lastMutationType: 'update-profile',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(state, UsersActions.updateProfileSucceeded());

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('update-profile');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('sets failed state when update profile fails', () => {
    const state: UsersState = {
      submitting: true,
      errorMessage: '',
      lastMutationType: 'update-profile',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(
      state,
      UsersActions.updateProfileFailed({
        errorMessage: 'This email is already used by another account.',
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('This email is already used by another account.');
    expect(next.lastMutationType).toBe('update-profile');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets submitting state when change password is requested', () => {
    const next = usersReducer(
      {
        ...initialState,
        errorMessage: 'Existing error',
      },
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      })
    );

    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('change-password');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets success state when change password succeeds', () => {
    const state: UsersState = {
      submitting: true,
      errorMessage: 'Old error',
      lastMutationType: 'change-password',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(state, UsersActions.changePasswordSucceeded());

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('change-password');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('sets failed state when change password fails', () => {
    const state: UsersState = {
      submitting: true,
      errorMessage: '',
      lastMutationType: 'change-password',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(
      state,
      UsersActions.changePasswordFailed({
        errorMessage: 'Current password is incorrect.',
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('Current password is incorrect.');
    expect(next.lastMutationType).toBe('change-password');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears only error message on clearError', () => {
    const state: UsersState = {
      submitting: false,
      errorMessage: 'Any error',
      lastMutationType: 'update-profile',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(state, UsersActions.clearError());

    expect(next.errorMessage).toBe('');
    expect(next.lastMutationType).toBe('update-profile');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears mutation status on clearMutationStatus', () => {
    const state: UsersState = {
      submitting: false,
      errorMessage: '',
      lastMutationType: 'change-password',
      lastMutationSucceeded: true,
    };

    const next = usersReducer(state, UsersActions.clearMutationStatus());

    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });
});
