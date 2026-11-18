import { Injectable } from '@angular/core';
import { OperatingAccessHint } from '../../billing/status/operating-access-hint.service';

export { OPERATING_ACCESS_ERROR_KEYS as SALE_LICENCE_ERROR_KEYS } from '../../billing/status/operating-access-hint.service';

@Injectable({ providedIn: 'root' })
export class SaleLicenceGateService extends OperatingAccessHint {}
