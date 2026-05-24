import { Injectable } from '@angular/core';

import { NewSalePageCoordinatorCoreService } from './new-sale-page.coordinator.core.service';

/**
 * Thin composition layer for the New Sale page.
 *
 * Business responsibilities live in focused coordinator helpers/services.
 */
@Injectable()
export class NewSalePageCoordinatorService extends NewSalePageCoordinatorCoreService {}
