import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class CameraStreamService {
  private currentStream: MediaStream | null = null;

  async startPreferredCamera(): Promise<MediaStream> {
    if (typeof window === 'undefined' || typeof location === 'undefined') {
      throw new Error('Camera access is only available in the browser.');
    }

    if (!window.isSecureContext && location.hostname !== 'localhost') {
      throw new Error(
        'Camera access on mobile needs HTTPS. Start the app with HTTPS or use localhost while testing.',
      );
    }

    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error(
        'Camera APIs are unavailable in this browser context. On mobile this usually means the page is not using HTTPS.',
      );
    }

    this.stopCurrentStream();

    const preferredConstraints: MediaStreamConstraints = {
      audio: false,
      video: {
        facingMode: { ideal: 'environment' },
        width: { ideal: 1280 },
        height: { ideal: 720 },
      },
    };

    try {
      this.currentStream = await navigator.mediaDevices.getUserMedia(preferredConstraints);
    } catch {
      this.currentStream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: true,
      });
    }

    return this.currentStream;
  }

  async attachToVideo(stream: MediaStream, videoElement: HTMLVideoElement): Promise<void> {
    videoElement.srcObject = stream;
    videoElement.muted = true;
    videoElement.autoplay = true;
    videoElement.setAttribute('playsinline', 'true');

    if (videoElement.readyState < HTMLMediaElement.HAVE_METADATA) {
      await new Promise<void>((resolve, reject) => {
        const onLoadedMetadata = (): void => {
          cleanup();
          resolve();
        };
        const onError = (): void => {
          cleanup();
          reject(new Error('Unable to start the camera preview.'));
        };
        const cleanup = (): void => {
          videoElement.removeEventListener('loadedmetadata', onLoadedMetadata);
          videoElement.removeEventListener('error', onError);
        };

        videoElement.addEventListener('loadedmetadata', onLoadedMetadata, { once: true });
        videoElement.addEventListener('error', onError, { once: true });
      });
    }

    await videoElement.play().catch(() => undefined);
  }

  detachVideo(videoElement: HTMLVideoElement | null | undefined): void {
    if (!videoElement) {
      return;
    }

    try {
      if (typeof videoElement.pause === 'function') {
        videoElement.pause();
      }
    } catch {
      // Ignore cleanup failures during view teardown.
    }

    try {
      videoElement.srcObject = null;
    } catch {
      // Ignore cleanup failures during view teardown.
    }
  }

  stopCurrentStream(): void {
    if (!this.currentStream) {
      return;
    }

    for (const track of this.currentStream.getTracks()) {
      track.stop();
    }

    this.currentStream = null;
  }
}
