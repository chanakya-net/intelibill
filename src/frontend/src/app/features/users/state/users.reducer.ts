import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { ShopUser } from '../services/user-account.service';
import { UserMutationType, UsersActions } from './users.actions';

export const usersFeatureKey = 'users';

export const usersAdapter = createEntityAdapter<ShopUser>({
  selectId: (user) => user.userId,
});

export interface UsersState extends EntityState<ShopUser> {
  readonly loadingShopUsers: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: UserMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: UsersState = usersAdapter.getInitialState({
  loadingShopUsers: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
});

export const usersReducer = createReducer(
  initialState,
  on(UsersActions.updateProfileRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-profile',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.updateProfileSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update-profile',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.updateProfileFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'update-profile',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.changePasswordRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'change-password',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.changePasswordSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'change-password',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.changePasswordFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'change-password',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.loadShopUsersRequested, (state) => ({
    ...state,
    loadingShopUsers: true,
    errorMessage: '',
  })),
  on(UsersActions.loadShopUsersSucceeded, (state, { users }) =>
    usersAdapter.setAll([...users], {
      ...state,
      loadingShopUsers: false,
      errorMessage: '',
    })
  ),
  on(UsersActions.loadShopUsersFailed, (state, { errorMessage }) => ({
    ...state,
    loadingShopUsers: false,
    errorMessage,
  })),

  on(UsersActions.addShopUserRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'add-shop-user',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.addShopUserSucceeded, (state, { user }) =>
    usersAdapter.addOne(user, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationType: 'add-shop-user',
      lastMutationSucceeded: true,
    })
  ),
  on(UsersActions.addShopUserFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'add-shop-user',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.editShopUserRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'edit-shop-user',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.editShopUserSucceeded, (state, { user }) =>
    usersAdapter.updateOne(
      {
        id: user.userId,
        changes: user,
      },
      {
        ...state,
        submitting: false,
        errorMessage: '',
        lastMutationType: 'edit-shop-user',
        lastMutationSucceeded: true,
      }
    )
  ),
  on(UsersActions.editShopUserFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'edit-shop-user',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(UsersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const usersFeature = createFeature({
  name: usersFeatureKey,
  reducer: usersReducer,
});
