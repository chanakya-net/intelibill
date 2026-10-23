import {
  selectPendingShopInvitations,
  selectShopInvitations,
  selectShopUsers,
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersLoadingShopUsers,
  selectUsersSubmitting,
} from './users.selectors';
import { UsersState } from './users.reducer';
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
  firstName: 'Staff',
  lastName: 'User',
  email: 'staff@test.com',
  phoneNumber: '+15551234568',
  role: 'Staff',
  status: 'Suspended',
  membershipVersion: 3,
  shopIds: ['shop-1'],
};

const pendingInvitation: ShopInvitation = {
  invitationId: 'i1',
  shopId: 'shop-1',
  email: 'invited@test.com',
  role: 'Manager',
  status: 'Pending',
  expiresAt: '2026-11-01T00:00:00Z',
  createdAt: '2026-10-25T00:00:00Z',
};

const revokedInvitation: ShopInvitation = {
  ...pendingInvitation,
  invitationId: 'i2',
  email: 'withdrawn@test.com',
  status: 'Revoked',
};

describe('users selectors', () => {
  const usersState: UsersState = {
    ids: ['u1', 'u2'],
    entities: {
      u1: ownerUser,
      u2: staffUser,
    },
    loadingShopUsers: true,
    submitting: true,
    errorMessage: 'errors.users.unableToLoadShopUsers',
    lastMutationType: 'update-membership',
    lastMutationSucceeded: true,
    invitations: [pendingInvitation, revokedInvitation],
  };

  const rootState = {
    users: usersState,
  };

  it('selects shop users list', () => {
    expect(selectShopUsers(rootState as never)).toEqual([ownerUser, staffUser]);
  });

  it('selects loading users state', () => {
    expect(selectUsersLoadingShopUsers(rootState as never)).toBe(true);
  });

  it('selects submitting users state', () => {
    expect(selectUsersSubmitting(rootState as never)).toBe(true);
  });

  it('selects users error message', () => {
    expect(selectUsersErrorMessage(rootState as never)).toBe('errors.users.unableToLoadShopUsers');
  });

  it('selects users last mutation type', () => {
    expect(selectUsersLastMutationType(rootState as never)).toBe('update-membership');
  });

  it('selects every invitation', () => {
    expect(selectShopInvitations(rootState as never)).toEqual([pendingInvitation, revokedInvitation]);
  });

  it('selects only invitations that can still be redeemed', () => {
    expect(selectPendingShopInvitations(rootState as never)).toEqual([pendingInvitation]);
  });

  it('selects users last mutation status', () => {
    expect(selectUsersLastMutationSucceeded(rootState as never)).toBe(true);
  });
});
