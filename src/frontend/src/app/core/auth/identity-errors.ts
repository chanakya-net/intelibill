import { ApiErrorPayload } from './auth.models';
export function identityError(error: { error?: ApiErrorPayload }): string {
  const code = error?.error?.code ?? error?.error?.title;
  switch (code) {
    case 'Auth.InvalidChallenge':
      return 'identity.invalidLink';
    case 'Auth.WrongChallengeActor':
      return 'identity.wrongAccount';
    case 'Auth.RecoverySupportRequired':
      return 'identity.supportRequired';
    case 'Auth.EmailDeliveryUnavailable':
    case 'Auth.EmailUnavailable':
      return 'identity.deliveryUnavailable';
    case 'Auth.AccountLinkRequired':
      return 'identity.collision';
    case 'Auth.LastSignInMethod':
      return 'identity.lastMethod';
    case 'Auth.InvalidCredentials':
      return 'errors.auth.invalidCredentials';
    case 'Auth.EmailVerificationRequired':
      return 'identity.verificationRequired';
    case 'Auth.InvitationRequired':
      return 'identity.invitationRequired';
    case 'Auth.EmailChangeRequired':
      return 'identity.emailChangeRequired';
    default:
      return 'identity.tryAgain';
  }
}

// Only these public outcome reasons may cross a callback redirect. Never route API
// descriptions, provider responses or bearer proof values.
export type IdentityFailureReason =
  | 'last-method'
  | 'support-required'
  | 'delivery-unavailable'
  | 'collision'
  | 'failed';

export function identityFailureReason(error?: { error?: ApiErrorPayload }): IdentityFailureReason {
  switch (identityError(error ?? {})) {
    case 'identity.lastMethod':
      return 'last-method';
    case 'identity.supportRequired':
      return 'support-required';
    case 'identity.deliveryUnavailable':
      return 'delivery-unavailable';
    case 'identity.collision':
      return 'collision';
    default:
      return 'failed';
  }
}

export function identityFailureMessage(reason: string | null): string | null {
  switch (reason) {
    case 'last-method':
      return 'identity.lastMethod';
    case 'support-required':
      return 'identity.supportRequired';
    case 'delivery-unavailable':
      return 'identity.deliveryUnavailable';
    case 'collision':
      return 'identity.collision';
    case 'failed':
      return 'identity.reauthFailed';
    default:
      return null;
  }
}
