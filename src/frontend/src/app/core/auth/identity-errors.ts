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
    case 'Auth.PhoneEnrollmentUnavailable':
      return 'identity.phoneUnavailableHere';
    case 'Auth.PhoneDeliveryUnavailable':
      return 'identity.phoneDeliveryUnavailable';
    case 'Auth.PhoneUnavailable':
      return 'identity.phoneTaken';
    case 'Auth.InvalidPhoneNumber':
      return 'identity.phoneInvalid';
    case 'Auth.PhoneChangeRequiresProof':
      return 'identity.phoneChangeRequiresProof';
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

// Only these public outcome reasons may reach the sign-in or account pages as a query
// string. Never route API descriptions or bearer proof values.
export function identityFailureMessage(reason: string | null): string | null {
  switch (reason) {
    case 'support-required':
      return 'identity.supportRequired';
    case 'delivery-unavailable':
      return 'identity.deliveryUnavailable';
    case 'failed':
      return 'identity.reauthFailed';
    default:
      return null;
  }
}
