import { test } from '@playwright/test';

import { ROUTE_MANIFEST } from '../route-manifest';
import {
  buildScenarioCatalog,
  runCoverageScenario,
} from '../support/css-coverage';

const COVERAGE_SCENARIOS = buildScenarioCatalog(ROUTE_MANIFEST);

test.describe('complete mocked Chromium CSS coverage catalog', () => {
  for (const scenario of COVERAGE_SCENARIOS) {
    test(scenario.id, async ({ context, page }, testInfo) => {
      test.skip(
        testInfo.project.name !== `chromium-${scenario.viewport}`,
        `scenario belongs to ${scenario.viewport} coverage`,
      );
      test.setTimeout(45_000);

      await runCoverageScenario({ context, page, scenario });
    });
  }
});
