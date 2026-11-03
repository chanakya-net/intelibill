import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable } from 'rxjs';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AccessScopeResult, ShopPreferenceResult, UserShop } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { ScopeMismatchError } from '../../../core/context/scoped-request';

export interface UpdateShopRequest {
  readonly name: string;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pincode: string;
  readonly contactPerson?: string;
  readonly mobileNumber?: string;
  readonly gstNumber?: string;
}

// ─── GST tax profile ──────────────────────────────────────────────────────────
// The registration a shop issues documents under. The regime is what the owner declares; the GSTIN only has to be
// consistent with it. Existing shops start as NotDeclared and cannot bill until the owner declares one.

export type ShopTaxRegime = 'NotDeclared' | 'Regular' | 'Composition' | 'Unregistered';
export type DeclarableTaxRegime = Exclude<ShopTaxRegime, 'NotDeclared'>;
export type EInvoiceStatus = 'NotAttested' | 'BelowThreshold' | 'Exempt' | 'Required';
export type AttestedEInvoiceStatus = Exclude<EInvoiceStatus, 'NotAttested'>;

export interface ShopTaxProfileDto {
  readonly legalName: string | null;
  readonly stateCode: string | null;
  readonly taxRegime: ShopTaxRegime;
  readonly invoiceSeriesCode: string;
  readonly eInvoiceStatus: EInvoiceStatus;
  readonly eInvoiceAttestedAt: string | null;
}

/** The tax profile sent with a new shop; its GSTIN is the request's top-level `gstNumber`. */
export interface CreateShopTaxProfileRequest {
  readonly legalName: string | null;
  readonly stateCode: string;
  readonly taxRegime: DeclarableTaxRegime;
  /** Null keeps the default series, A. */
  readonly invoiceSeriesCode: string | null;
  /** Required for a Regular shop, null otherwise. */
  readonly eInvoiceStatus: AttestedEInvoiceStatus | null;
}

/** PUT shops/{shopId}/tax-profile (owner only). */
export interface UpdateShopTaxProfileRequest extends CreateShopTaxProfileRequest {
  readonly gstNumber: string | null;
}

export interface CreateShopRequest extends UpdateShopRequest {
  readonly taxProfile?: CreateShopTaxProfileRequest;
}

export type ShopRole = string;

export interface ShopMemberDto {
  readonly userId: string;
  readonly fullName?: string;
  readonly firstName?: string;
  readonly lastName?: string;
  readonly email?: string | null;
  readonly phoneNumber?: string | null;
  readonly role: ShopRole;
}

export interface ShopDetails {
  readonly shopId: string;
  readonly name: string;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pincode: string;
  readonly contactPerson: string | null;
  readonly mobileNumber: string | null;
  readonly gstNumber: string | null;
  readonly bankName: string | null;
  readonly bankAccountNumber: string | null;
  readonly bankAccountType: string | null;
  readonly ifscCode: string | null;
  readonly accountHolderName: string | null;
  readonly logoUrl?: string | null;
  readonly members?: readonly ShopMemberDto[] | null;
  /** Absent only from responses that predate tax profiles. */
  readonly taxProfile?: ShopTaxProfileDto;
}

export type ShopDetailsDto = ShopDetails;

export interface UpdateBankDetailsRequest {
  readonly bankName?: string;
  readonly accountNumber?: string;
  readonly accountType?: string;
  readonly ifscCode?: string;
  readonly accountHolderName?: string;
}

interface SetDefaultShopRequest {
  readonly shopId: string;
}

interface SwitchActiveShopRequest {
  readonly shopId: string;
}

@Injectable({ providedIn: 'root' })
export class ShopService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);

  getMyShops(): Observable<readonly UserShop[]> {
    return this.http.get<readonly UserShop[]>(SHOP_ENDPOINTS.me);
  }

  getShopDetails(shopId: string): Observable<ShopDetails> {
    return this.http.get<ShopDetails>(SHOP_ENDPOINTS.details(shopId));
  }

  createShop(payload: CreateShopRequest): Observable<void> {
    // A created shop moves this device into it and reissues only its access token: creating a shop
    // is not a sign-in, so it hands out no new refresh credential.
    return this.http
      .post<AccessScopeResult>(SHOP_ENDPOINTS.create, payload)
      .pipe(this.adoptScope());
  }

  /**
   * Moves this device into another of the caller's shops. The server rescopes this device's
   * session and reissues only its access token: choosing a shop is not a credential event, so the
   * refresh credential this sign-in earned is left exactly as it is. No other device moves.
   */
  switchActiveShop(shopId: string): Observable<void> {
    const payload: SwitchActiveShopRequest = { shopId };

    return this.http
      .post<AccessScopeResult>(SHOP_ENDPOINTS.switch, payload)
      .pipe(this.adoptScope());
  }

  setDefaultShop(shopId: string): Observable<void> {
    const payload: SetDefaultShopRequest = { shopId };

    // Saving the default is a stored preference: this device keeps working in the shop it is in.
    return this.http.post<ShopPreferenceResult>(SHOP_ENDPOINTS.setDefault, payload).pipe(
      map((result) => this.authService.applyShopPreference(result))
    );
  }

  updateShop(shopId: string, payload: UpdateShopRequest): Observable<ShopDetails> {
    return this.http.put<ShopDetails>(SHOP_ENDPOINTS.update(shopId), payload);
  }

  updateTaxProfile(shopId: string, payload: UpdateShopTaxProfileRequest): Observable<ShopDetails> {
    return this.http.put<ShopDetails>(SHOP_ENDPOINTS.taxProfile(shopId), payload);
  }

  updateBankDetails(shopId: string, payload: UpdateBankDetailsRequest): Observable<ShopDetails> {
    return this.http.put<ShopDetails>(SHOP_ENDPOINTS.bankDetails(shopId), payload);
  }

  /**
   * Installs a scope answer, but only into the device session that asked for it and only when no
   * newer scope was installed meanwhile; otherwise the switch is reported as failed.
   */
  private adoptScope(): (source: Observable<AccessScopeResult>) => Observable<void> {
    const requestedFrom = this.authService.sessionIdentity();
    return (source) =>
      source.pipe(
        map((result) => {
          if (!this.authService.applyAccessScope(result, requestedFrom)) {
            throw new ScopeMismatchError();
          }
        })
      );
  }
}
