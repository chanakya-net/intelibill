import { Injectable } from '@angular/core';

interface AudioContextWindow extends Window {
  webkitAudioContext?: typeof AudioContext;
}

@Injectable({
  providedIn: 'root',
})
export class AudioService {
  private audioContext?: AudioContext;

  async prime(): Promise<void> {
    if (!this.supportsAudioContext()) {
      return;
    }

    if (!this.audioContext) {
      this.audioContext = this.createAudioContext();
    }

    if (this.audioContext.state === 'suspended') {
      await this.audioContext.resume();
    }
  }

  async beep(): Promise<void> {
    await this.prime();

    if (!this.audioContext) {
      return;
    }

    const oscillator = this.audioContext.createOscillator();
    const gainNode = this.audioContext.createGain();
    const startTime = this.audioContext.currentTime;
    const durationSeconds = 0.11;

    oscillator.type = 'square';
    oscillator.frequency.setValueAtTime(880, startTime);

    gainNode.gain.setValueAtTime(0.001, startTime);
    gainNode.gain.exponentialRampToValueAtTime(0.18, startTime + 0.01);
    gainNode.gain.exponentialRampToValueAtTime(0.001, startTime + durationSeconds);

    oscillator.connect(gainNode);
    gainNode.connect(this.audioContext.destination);

    oscillator.start(startTime);
    oscillator.stop(startTime + durationSeconds);
  }

  private supportsAudioContext(): boolean {
    const audioWindow = window as AudioContextWindow;
    return typeof window.AudioContext !== 'undefined' || typeof audioWindow.webkitAudioContext !== 'undefined';
  }

  private createAudioContext(): AudioContext {
    const audioWindow = window as AudioContextWindow;
    const AudioContextConstructor = window.AudioContext ?? audioWindow.webkitAudioContext;

    if (!AudioContextConstructor) {
      throw new Error('Web Audio is not supported on this browser.');
    }

    return new AudioContextConstructor();
  }
}