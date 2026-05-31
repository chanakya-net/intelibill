export function createBlobUrl(blob: Blob): string {
  if (typeof URL.createObjectURL !== 'function') {
    throw new Error('Blob URL creation is not supported in this environment');
  }

  return URL.createObjectURL(blob);
}

export function openPdfBlobInNewTab(blob: Blob): void {
  const url = createBlobUrl(blob);
  const opened = window.open(url, '_blank');

  if (!opened) {
    URL.revokeObjectURL(url);
    throw new Error('Unable to open blob URL in a new tab');
  }

  window.setTimeout(() => {
    URL.revokeObjectURL(url);
  }, 1000);
}

export function downloadBlob(blob: Blob, filename: string): void {
  const url = createBlobUrl(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
