export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly code?: string;
  readonly detail?: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;
}

export function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) return null;
  const problem = body as ProblemDetails;
  const code = problem.code ?? problem.title;
  return typeof code === 'string' && code.trim() ? code : null;
}
