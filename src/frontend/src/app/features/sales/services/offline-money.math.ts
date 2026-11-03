// Offline invoice calculation contract version 1: money in paise (scale 2), quantity in thousandths (scale 3),
// percentages in basis points (scale 2); every division rounds half away from zero. The server replays the
// same integer arithmetic (OfflineInvoiceArithmeticV1.cs), pinned by tests/contracts/offline-invoice-arithmetic-v1.json.
export const OFFLINE_CALCULATION_VERSION = 1;

export const MONEY_SCALE = 2;
export const QUANTITY_SCALE = 3;
export const PERCENT_SCALE = 2;

const DECIMAL_TEXT = /^(-?)(\d+)(?:\.(\d+))?(?:e([+-]?\d+))?$/i;

export function roundDiv(numerator: bigint, denominator: bigint): bigint {
  if (denominator <= 0n) throw new RangeError('Denominator must be positive.');
  const magnitude = numerator < 0n ? -numerator : numerator;
  let quotient = magnitude / denominator;
  if ((magnitude % denominator) * 2n >= denominator) quotient += 1n;
  return numerator < 0n ? -quotient : quotient;
}

// Parses the number's shortest decimal text (what JSON carries), so 0.1 is exactly one tenth, then rounds to the scale.
export function toScaled(value: number, scale: number): bigint {
  if (!Number.isFinite(value)) throw new RangeError(`Amount ${value} is not a finite number.`);
  const match = DECIMAL_TEXT.exec(String(value));
  if (!match) throw new RangeError(`Amount ${value} has an unsupported format.`);

  const [, sign, integerDigits, fractionDigits = '', exponent = '0'] = match;
  const digits = BigInt(integerDigits + fractionDigits);
  const shift = Number(exponent) - fractionDigits.length + scale;
  const magnitude = shift >= 0 ? digits * 10n ** BigInt(shift) : roundDiv(digits, 10n ** BigInt(-shift));
  return sign === '-' ? -magnitude : magnitude;
}

export function fromScaled(value: bigint, scale: number): number {
  return Number(value) / 10 ** scale;
}

export function normalizeScale(value: number, scale: number): number {
  return fromScaled(toScaled(value, scale), scale);
}

export function percentOf(basis: bigint, basisPoints: bigint): bigint {
  return roundDiv(basis * basisPoints, 10_000n);
}

export function minBigInt(a: bigint, b: bigint): bigint {
  return a < b ? a : b;
}

export function clampBigInt(value: bigint, max: bigint): bigint {
  const floored = value < 0n ? 0n : value;
  return floored > max ? max : floored;
}

// Floor share per weight, capped at the weight itself; leftover units go to the largest remainders, ties by position.
export function allocateLargestRemainder(total: bigint, weights: readonly bigint[]): bigint[] {
  const allocation = weights.map(() => 0n);
  const weightSum = weights.reduce((sum, weight) => sum + (weight > 0n ? weight : 0n), 0n);
  if (total <= 0n || weightSum <= 0n) return allocation;

  const remainders = weights.map(() => 0n);
  let remaining = total;
  weights.forEach((weight, index) => {
    if (weight <= 0n) return;
    const share = (total * weight) / weightSum;
    remainders[index] = (total * weight) % weightSum;
    allocation[index] = minBigInt(share, weight);
    remaining -= allocation[index];
  });

  const order = weights
    .map((weight, index) => ({ weight, index }))
    .filter(({ weight }) => weight > 0n)
    .sort((a, b) => (remainders[a.index] === remainders[b.index] ? a.index - b.index : remainders[a.index] > remainders[b.index] ? -1 : 1))
    .map(({ index }) => index);

  while (remaining > 0n) {
    let progressed = false;
    for (const index of order) {
      if (remaining === 0n) break;
      if (allocation[index] >= weights[index]) continue;
      allocation[index] += 1n;
      remaining -= 1n;
      progressed = true;
    }
    if (!progressed) break;
  }

  return allocation;
}
