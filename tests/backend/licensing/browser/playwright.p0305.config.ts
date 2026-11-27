import { defineConfig } from '@playwright/test';
import fixture from './playwright.p0304.config';

export default defineConfig({ ...fixture, testMatch: /p03-05-webhook-status\.spec\.ts$/ });
