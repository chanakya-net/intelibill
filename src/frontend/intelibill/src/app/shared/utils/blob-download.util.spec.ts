import { describe, expect, it, vi } from 'vitest';

import { createBlobUrl, downloadBlob, openPdfBlobInNewTab } from './blob-download.util';

describe('blob file util', () => {
  it('opens a blob URL in a new tab and revokes it after a timeout', () => {
    vi.useFakeTimers();
    const openWindow = vi.fn(() => ({} as Window));
    const createSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-open');
    const revokeSpy = vi.spyOn(URL, 'revokeObjectURL');
    const openSpy = vi.spyOn(window, 'open').mockImplementation(openWindow);
    const blob = new Blob(['pdf-data'], { type: 'application/pdf' });

    openPdfBlobInNewTab(blob);

    expect(createSpy).toHaveBeenCalledWith(blob);
    expect(openSpy).toHaveBeenCalledWith('blob:mock-open', '_blank');
    expect(revokeSpy).not.toHaveBeenCalled();

    vi.runAllTimers();
    expect(revokeSpy).toHaveBeenCalledWith('blob:mock-open');

    openSpy.mockRestore();
    createSpy.mockRestore();
    revokeSpy.mockRestore();
    vi.useRealTimers();
  });

  it('creates and clicks an anchor link to download a blob', () => {
    const createSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-download');
    const revokeSpy = vi.spyOn(URL, 'revokeObjectURL');
    const createElementSpy = vi.spyOn(document, 'createElement');
    const appendSpy = vi.spyOn(document.body, 'appendChild');
    const removeSpy = vi.spyOn(document.body, 'removeChild');
    const blob = new Blob(['pdf-data'], { type: 'application/pdf' });

    const anchor = document.createElement('a');
    const anchorClick = vi.fn();
    anchor.click = anchorClick;
    createElementSpy.mockReturnValue(anchor);

    downloadBlob(blob, 'labels.pdf');

    expect(createSpy).toHaveBeenCalledWith(blob);
    expect(createElementSpy).toHaveBeenCalledWith('a');
    expect(appendSpy).toHaveBeenCalledWith(anchor);
    expect(removeSpy).toHaveBeenCalledWith(anchor);
    expect(anchorClick).toHaveBeenCalledTimes(1);
    expect(anchor.href).toBe('blob:mock-download');
    expect(anchor.download).toBe('labels.pdf');
    expect(revokeSpy).toHaveBeenCalledWith('blob:mock-download');

    createSpy.mockRestore();
    revokeSpy.mockRestore();
    createElementSpy.mockRestore();
    appendSpy.mockRestore();
    removeSpy.mockRestore();
  });

  it('builds a blob URL when createObjectURL exists', () => {
    const spy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:typed');

    const result = createBlobUrl(new Blob(['typed'], { type: 'application/pdf' }));

    expect(result).toBe('blob:typed');
    spy.mockRestore();
  });
});
