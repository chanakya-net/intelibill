import { SIMULATOR_FLOW, type CheckoutLaunch } from './checkout-payment.models';
import { razorpayLaunch } from './razorpay-checkout';

export type LauncherKind = 'simulator' | 'razorpay' | 'unsupported';

/**
 * Resolves the launcher for a server-declared flow. Only allow-listed flows with their exact allow-listed public
 * parameters open; anything else (including a gateway-supplied URL or script) is refused.
 */
export function resolvePaymentLauncher(launch: CheckoutLaunch | null): LauncherKind | null {
  if (!launch) return null;
  if (launch.flow === SIMULATOR_FLOW) return Object.keys(launch.parameters ?? {}).length === 0 ? 'simulator' : 'unsupported';
  return razorpayLaunch(launch) ? 'razorpay' : 'unsupported';
}
