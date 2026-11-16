import { SIMULATOR_FLOW, type CheckoutLaunch } from './checkout-payment.models';

export type LauncherKind = 'simulator' | 'unsupported';

/**
 * Resolves the launcher for a server-declared flow. Only allow-listed flows open; anything else (including a
 * gateway-supplied URL or script) is refused. Real provider launchers are added beside the simulator later.
 */
export function resolvePaymentLauncher(launch: CheckoutLaunch | null): LauncherKind | null {
  if (!launch) return null;
  return launch.flow === SIMULATOR_FLOW && Object.keys(launch.parameters ?? {}).length === 0 ? 'simulator' : 'unsupported';
}
