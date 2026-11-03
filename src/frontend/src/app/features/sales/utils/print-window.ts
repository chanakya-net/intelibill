/**
 * Opens a print view in a new tab. Returns null when it opened, or the URL itself when the browser blocked the window,
 * so the caller can offer it as a link the cashier opens by hand.
 */
export function openPrintWindow(url: string): string | null {
  return window.open(url, '_blank') ? null : url;
}
