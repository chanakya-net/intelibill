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
