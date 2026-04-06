import { usersReducer, UsersState } from './users.reducer';
import { UsersActions } from './users.actions';
import { ShopUser } from '../services/user-account.service';

const ownerUser: ShopUser = {
  userId: 'u1',
  firstName: 'Owner',
  lastName: 'User',
  email: 'owner@test.com',
  phoneNumber: '+15551234567',
  role: 'Owner',
  isLoginEnabled: true,
  shopIds: ['shop-1'],
};

const staffUser: ShopUser = {
  userId: 'u2',
  firstName: 'Sales',
  lastName: 'Rep',
  email: null,
  phoneNumber: '+15557654321',
  role: 'Staff',
  isLoginEnabled: true,
  shopIds: ['shop-1'],
};

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

  it('sets loading state when shop users load is requested', () => {
    const next = usersReducer(initialState, UsersActions.loadShopUsersRequested());

    expect(next.loadingShopUsers).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('stores users when shop users load succeeds', () => {
    const next = usersReducer(
      {
        ...initialState,
        loadingShopUsers: true,
      },
      UsersActions.loadShopUsersSucceeded({
        users: [ownerUser],
      })
    );

    expect(next.loadingShopUsers).toBe(false);
    expect(next.ids).toEqual(['u1']);
    expect(next.entities['u1']).toEqual(ownerUser);
  });

  it('sets success state when update profile succeeds', () => {
    const state: UsersState = {
      ids: [],
      entities: {},
      loadingShopUsers: false,
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
      ids: [],
      entities: {},
      loadingShopUsers: false,
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
      ids: [],
      entities: {},
      loadingShopUsers: false,
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
      ids: [],
      entities: {},
      loadingShopUsers: false,
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

  it('appends user when add shop user succeeds', () => {
    const state: UsersState = {
      ...initialState,
      submitting: true,
      lastMutationType: 'add-shop-user',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(
      state,
      UsersActions.addShopUserSucceeded({
        user: staffUser,
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('add-shop-user');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.ids).toEqual(['u2']);
    expect(next.entities['u2']).toEqual(staffUser);
  });

  it('replaces matching user when edit shop user succeeds', () => {
    const state: UsersState = {
      ...initialState,
      ids: ['u2'],
      entities: {
        u2: staffUser,
      },
      submitting: true,
      lastMutationType: 'edit-shop-user',
      lastMutationSucceeded: false,
    };

    const next = usersReducer(
      state,
      UsersActions.editShopUserSucceeded({
        user: {
          ...staffUser,
          lastName: 'User',
          role: 'Manager',
          isLoginEnabled: false,
        },
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('edit-shop-user');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.entities['u2']?.role).toBe('Manager');
    expect(next.entities['u2']?.isLoginEnabled).toBe(false);
  });

  it('clears only error message on clearError', () => {
    const state: UsersState = {
      ids: [],
      entities: {},
      loadingShopUsers: false,
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
      ids: [],
      entities: {},
      loadingShopUsers: false,
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
