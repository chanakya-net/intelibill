/**
 * Whether an HTTP error's problem body carries this error code. A handler's error answers with the code as the
 * problem `title`; validator failures answer with the title Validation.Failed and each code in `errors[].code`.
 */
export function hasProblemCode(error: unknown, code: string): boolean {
  const body = (error as { error?: { title?: unknown; errors?: unknown } } | null)?.error;
  if (!body || typeof body !== 'object') {
    return false;
  }

  if (body.title === code) {
    return true;
  }

  return (
    Array.isArray(body.errors) &&
    body.errors.some((entry) => typeof entry === 'object' && entry !== null && (entry as { code?: unknown }).code === code)
  );
}
