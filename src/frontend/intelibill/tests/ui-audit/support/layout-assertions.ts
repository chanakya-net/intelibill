import type { Page } from '@playwright/test';

export const AUDIT_VIEWPORTS = [
  { name: 'mobile', width: 360, height: 800 },
  { name: 'desktop', width: 1440, height: 900 },
] as const;

type Viewport = (typeof AUDIT_VIEWPORTS)[number];

export interface LayoutMetrics {
  readonly viewport: Pick<Viewport, 'width' | 'height'>;
  readonly documentWidth: number;
  readonly card: {
    readonly left: number;
    readonly right: number;
    readonly top: number;
    readonly bottom: number;
  } | null;
}

export function validateLayoutMetrics(metrics: LayoutMetrics): string[] {
  const issues: string[] = [];
  const { viewport, documentWidth, card } = metrics;

  if (documentWidth > viewport.width) {
    issues.push(`document width ${documentWidth} exceeds viewport width ${viewport.width}`);
  }

  if (
    !card ||
    card.left < 0 ||
    card.right > viewport.width ||
    card.top < 0 ||
    card.bottom > viewport.height
  ) {
    issues.push('auth card is outside the viewport bounds');
  }

  return issues;
}

export async function assertLoginLayout(page: Page, viewport: Viewport): Promise<void> {
  const metrics = await page.evaluate(() => {
    const card = document.querySelector<HTMLElement>('.auth-card')?.getBoundingClientRect();
    return {
      documentWidth: document.documentElement.scrollWidth,
      card: card
        ? { left: card.left, right: card.right, top: card.top, bottom: card.bottom }
        : null,
    };
  });

  const issues = validateLayoutMetrics({ viewport, ...metrics });
  if (issues.length > 0) {
    throw new Error(`login layout contract failed: ${issues.join('; ')}`);
  }
}
