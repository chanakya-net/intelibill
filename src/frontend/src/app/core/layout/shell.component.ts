import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { AuthService } from '../auth/auth.service';
import { CreateShopOverlayComponent } from '../../features/shops/components/create-shop-overlay.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, TagModule, ButtonModule, CreateShopOverlayComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly authService = inject(AuthService);

  readonly isSigningOut = signal(false);
  readonly showCreateShopOverlay = this.authService.needsShopSetup;

  onSignOut(): void {
    if (this.isSigningOut()) {
      return;
    }

    this.isSigningOut.set(true);
    this.authService.signOutAndRedirect().subscribe({
      complete: () => {
        this.isSigningOut.set(false);
      },
    });
  }
}
