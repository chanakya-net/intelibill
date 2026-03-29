import { selectShopUsers, selectUsersLoadingShopUsers } from './users.selectors';
import { UsersState } from './users.reducer';

describe('users selectors', () => {
  const usersState: UsersState = {
    shopUsers: [
      {
        userId: 'u1',
        firstName: 'Owner',
        lastName: 'User',
        email: 'owner@test.com',
        phoneNumber: '+15551234567',
        role: 'Owner',
      },
    ],
    loadingShopUsers: true,
    submitting: false,
    errorMessage: '',
    lastMutationType: null,
    lastMutationSucceeded: false,
  };

  const rootState = {
    users: usersState,
  };

  it('selects shop users list', () => {
    expect(selectShopUsers(rootState as never)).toEqual(usersState.shopUsers);
  });

  it('selects loading users state', () => {
    expect(selectUsersLoadingShopUsers(rootState as never)).toBe(true);
  });
});
