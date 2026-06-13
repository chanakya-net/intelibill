import { Injectable } from '@angular/core';

import { NewSalePageCreditNoteService } from './new-sale-page.credit-note.service';

/**
 * Thin composition layer for the New Sale page.
 *
 * Business responsibilities live in focused page-flow services.
 */
@Injectable()
export class NewSalePageCoordinatorService extends NewSalePageCreditNoteService {}
