import { usersReducer, UsersState } from './users.reducer';
import { UsersActions } from './users.actions';
import { ShopInvitation, ShopUser } from '../services/user-account.service';

const ownerUser: ShopUser = {
  userId: 'u1',
  firstName: 'Owner',
  lastName: 'User',
  email: 'owner@test.com',
  phoneNumber: '+15551234567',
  role: 'Owner',
  status: 'Active',
  membershipVersion: 0,
  shopIds: ['shop-1'],
};

const staffUser: ShopUser = {
  userId: 'u2',
  firstName: 'Sales',
  lastName: 'Rep',
  email: null,
  phoneNumber: '+15557654321',
  role: 'Staff',
  status: 'Active',
  membershipVersion: 1,
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
      invitations: [],
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
      invitations: [],
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
      invitations: [],
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
      invitations: [],
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

  it('records a sent invitation and supersedes an earlier one for the same address', () => {
    const first: ShopInvitation = {
      invitationId: 'i1',
      shopId: 'shop-1',
      email: 'invited@test.com',
      role: 'Staff',
      status: 'Pending',
      expiresAt: '2026-11-01T00:00:00Z',
      createdAt: '2026-10-25T00:00:00Z',
    };
    const resent: ShopInvitation = {
      ...first,
      invitationId: 'i2',
      role: 'Manager',
      createdAt: '2026-10-26T00:00:00Z',
    };

    const next = usersReducer(
      { ...initialState, submitting: true, invitations: [first] },
      UsersActions.inviteMemberSucceeded({ invitation: resent })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('invite-member');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.invitations).toEqual([resent]);
  });

  it('marks an invitation revoked when it is withdrawn', () => {
    const invitation: ShopInvitation = {
      invitationId: 'i1',
      shopId: 'shop-1',
      email: 'invited@test.com',
      role: 'Manager',
      status: 'Pending',
      expiresAt: '2026-11-01T00:00:00Z',
      createdAt: '2026-10-25T00:00:00Z',
    };

    const next = usersReducer(
      { ...initialState, invitations: [invitation], submitting: true },
      UsersActions.revokeInvitationSucceeded({ invitationId: 'i1' })
    );

    expect(next.invitations[0]?.status).toBe('Revoked');
    expect(next.lastMutationType).toBe('revoke-invitation');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('applies the new authority and version when a membership changes', () => {
    const next = usersReducer(
      {
        ...initialState,
        ids: ['u2'],
        entities: { u2: staffUser },
        submitting: true,
      },
      UsersActions.updateMembershipSucceeded({
        membership: {
          userId: 'u2',
          shopId: 'shop-1',
          displayName: 'Sales Rep',
          role: 'Manager',
          status: 'Suspended',
          version: 2,
        },
      })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationType).toBe('update-membership');
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.entities['u2']?.role).toBe('Manager');
    expect(next.entities['u2']?.status).toBe('Suspended');
    // The version comes back so the next change is checked against it, not against a stale one.
    expect(next.entities['u2']?.membershipVersion).toBe(2);
  });

  it('drops a removed membership from the directory', () => {
    const next = usersReducer(
      { ...initialState, ids: ['u2'], entities: { u2: staffUser } },
      UsersActions.updateMembershipSucceeded({
        membership: {
          userId: 'u2',
          shopId: 'shop-1',
          displayName: 'Sales Rep',
          role: 'Staff',
          status: 'Removed',
          version: 2,
        },
      })
    );

    expect(next.ids).toEqual([]);
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
      invitations: [],
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
      invitations: [],
    };

    const next = usersReducer(state, UsersActions.clearMutationStatus());

    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });
});
