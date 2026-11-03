import { afterEach, describe, expect, it, vi } from 'vitest';

import { openPrintWindow } from './print-window';

describe('openPrintWindow', () => {
  afterEach(() => vi.restoreAllMocks());

  it('opens the print view in a new tab and reports nothing to recover', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue({} as Window);

    expect(openPrintWindow('/sales/s-1/print?offline=1')).toBeNull();
    expect(open).toHaveBeenCalledWith('/sales/s-1/print?offline=1', '_blank');
  });

  it('hands back the URL when the browser blocks the window, so the cashier can still open it', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);

    expect(openPrintWindow('/sales/s-1/print?offline=1')).toBe('/sales/s-1/print?offline=1');
  });
});
