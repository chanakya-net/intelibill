import { Injectable } from '@angular/core';

import { NewSalePageOfflineFlowService } from './new-sale-page.offline-flow.service';

/**
 * Thin composition layer for the New Sale page.
 *
 * Business responsibilities live in focused page-flow services.
 */
@Injectable()
export class NewSalePageCoordinatorService extends NewSalePageOfflineFlowService {}
