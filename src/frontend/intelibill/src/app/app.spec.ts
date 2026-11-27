import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProductCatalogSyncService } from './core/services/product-catalog-sync.service';
import { App } from './app';
import { ShopDataAccessService } from './core/context/shop-data-access.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: ShopDataAccessService, useValue: {} },
        {
          provide: ProductCatalogSyncService,
          useValue: {
            catalogEntries: () => [],
            filterByName: () => [],
            filterByBarcode: () => [],
            findByName: () => undefined,
            findByBarcode: () => undefined,
          },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render router outlet shell', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });
});
