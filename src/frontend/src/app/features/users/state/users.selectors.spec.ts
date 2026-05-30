import {
  selectShopUsers,
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersLoadingShopUsers,
  selectUsersSubmitting,
} from './users.selectors';
import { UsersState } from './users.reducer';
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
  firstName: 'Staff',
  lastName: 'User',
  email: 'staff@test.com',
  phoneNumber: '+15551234568',
  role: 'Staff',
  isLoginEnabled: true,
  shopIds: ['shop-1'],
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
    lastMutationType: 'add-shop-user',
    lastMutationSucceeded: true,
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
    expect(selectUsersLastMutationType(rootState as never)).toBe('add-shop-user');
  });

  it('selects users last mutation status', () => {
    expect(selectUsersLastMutationSucceeded(rootState as never)).toBe(true);
  });
});
