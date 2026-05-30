import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ProductCatalogSyncService } from './core/services/product-catalog-sync.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly _catalogSync = inject(ProductCatalogSyncService);
}
