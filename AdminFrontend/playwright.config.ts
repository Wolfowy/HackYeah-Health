import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: process.env.ADMIN_TEST_URL || 'http://127.0.0.1:5174',
    trace: 'retain-on-failure',
    timezoneId: 'Europe/Warsaw',
  },
  projects: [
    {
      name: 'desktop',
      use: {
        ...devices['Desktop Chrome'],
        channel: 'chrome',
        viewport: { width: 1440, height: 1000 },
      },
    },
    {
      name: 'mobile',
      use: {
        ...devices['iPhone 13'],
        browserName: 'chromium',
        channel: 'chrome',
      },
    },
  ],
  webServer: {
    command: `npm run dev -- --strictPort --port ${process.env.ADMIN_TEST_PORT || '5174'}`,
    url: process.env.ADMIN_TEST_URL || 'http://127.0.0.1:5174',
    reuseExistingServer: !process.env.CI,
  },
})
