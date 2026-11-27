import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { filter, map } from 'rxjs';
import { ShopContextService } from './core/context/shop-context.service';
import { ShopDataAccessService } from './core/context/shop-data-access.service';
import { ProductCatalogSyncService } from './core/services/product-catalog-sync.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, TranslocoPipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly _accessObservation = inject(ShopDataAccessService);
  private readonly context = inject(ShopContextService);
  private readonly router = inject(Router);
  private readonly standaloneDataRoute = toSignal(this.router.events.pipe(
    filter((event): event is NavigationEnd => event instanceof NavigationEnd),
    map(() => this.router.routerState.snapshot.root.firstChild?.data['standaloneShopData'] === true),
  ), { initialValue: false });

  readonly hideStandaloneShopData = computed(() =>
    this.standaloneDataRoute() && (this.context.dataRestricted() || this.context.transitioning()));

  private readonly _catalogSync = inject(ProductCatalogSyncService);
}
