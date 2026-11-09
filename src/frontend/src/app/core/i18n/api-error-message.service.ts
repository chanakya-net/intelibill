import { Injectable } from '@angular/core';

export interface LocalizedFieldError {
  readonly field: string;
  readonly messageKey: string;
  readonly params: Readonly<Record<string, string | number>>;
}

export interface LocalizedError {
  readonly messageKey: string;
  readonly params: Readonly<Record<string, string | number>>;
  readonly fieldErrors: readonly LocalizedFieldError[];
  readonly correlationId?: string;
}

export interface ApiErrorContext {
  readonly fallbackKey: string;
  readonly codeMap?: Readonly<Record<string, string>>;
  readonly fieldMap?: Readonly<Record<string, string>>;
}

const COMMON_CODES: Readonly<Record<string, string>> = {
  'Validation.Failed': 'apiErrors.validationFailed',
  'Request.Invalid': 'apiErrors.invalidRequest',
  'Auth.Unauthorized': 'apiErrors.unauthorized',
  'Concurrency.Conflict': 'apiErrors.conflict',
  'Internal.Unexpected': 'apiErrors.unexpected',
  'Operation.PayloadMismatch': 'apiErrors.payloadMismatch',
  'PostingPeriod.Closed': 'apiErrors.postingPeriodClosed',
  'PostingPeriod.Busy': 'apiErrors.postingPeriodBusy',
  'PostingPeriod.NotEnded': 'apiErrors.postingPeriodNotEnded',
};

const SAFE_REFERENCE = /^[A-Za-z0-9._:-]{1,128}$/;

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function mapped(
  map: Readonly<Record<string, string>> | undefined,
  key: unknown,
): string | undefined {
  return typeof key === 'string' && map && Object.hasOwn(map, key) ? map[key] : undefined;
}

@Injectable({ providedIn: 'root' })
export class ApiErrorMessageService {
  resolve(problem: unknown, context: ApiErrorContext): LocalizedError {
    const wrapper = record(problem);
    const body = record(wrapper?.['error']) ?? wrapper;
    const code = body?.['code'];
    const title = body?.['title'];
    const hasCode = typeof code === 'string' && code.trim().length > 0;
    const messageKey = hasCode
      ? (mapped(context.codeMap, code) ?? mapped(COMMON_CODES, code) ?? context.fallbackKey)
      : (mapped(context.codeMap, title) ??
        mapped(COMMON_CODES, title) ??
        this.transportKey(wrapper) ??
        context.fallbackKey);

    const fieldErrors = this.fields(body?.['errors'], context);
    const reference =
      body?.['traceId'] ??
      body?.['correlationId'] ??
      wrapper?.['traceId'] ??
      wrapper?.['correlationId'];
    const correlationId =
      typeof reference === 'string' && SAFE_REFERENCE.test(reference) ? reference : undefined;

    return {
      messageKey,
      params: {},
      fieldErrors,
      ...(correlationId ? { correlationId } : {}),
    };
  }

  private transportKey(wrapper: Record<string, unknown> | null): string | undefined {
    if (!wrapper || !Object.hasOwn(wrapper, 'status')) return undefined;
    if (wrapper['status'] === 0) return 'apiErrors.networkUnavailable';
    if (wrapper['status'] === 408) return 'apiErrors.requestTimedOut';
    if (wrapper['status'] === 503) return 'apiErrors.serviceUnavailable';
    return undefined;
  }

  private fields(value: unknown, context: ApiErrorContext): LocalizedFieldError[] {
    if (!context.fieldMap) return [];
    const paths = Array.isArray(value)
      ? value.map((entry: unknown) => record(entry)?.['code'])
      : Object.keys(record(value) ?? {});
    const seen = new Set<string>();
    const result: LocalizedFieldError[] = [];
    for (const path of paths) {
      const field = mapped(context.fieldMap, path);
      if (!field || seen.has(field)) continue;
      seen.add(field);
      result.push({ field, messageKey: 'apiErrors.invalidField', params: {} });
    }
    return result;
  }
}
