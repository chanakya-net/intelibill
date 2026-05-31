import { Injectable } from '@angular/core';

import { downloadBlob, openPdfBlobInNewTab } from './blob-download.util';

@Injectable({ providedIn: 'root' })
export class BlobDownloadService {
  openPdf(blob: Blob): void {
    openPdfBlobInNewTab(blob);
  }

  download(blob: Blob, filename: string): void {
    downloadBlob(blob, filename);
  }
}

