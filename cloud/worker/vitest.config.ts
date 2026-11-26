import { cloudflareTest } from "@cloudflare/vitest-pool-workers";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [
    cloudflareTest({
      wrangler: { configPath: "./wrangler.jsonc" },
      miniflare: {
        bindings: { BOOTSTRAP_KEY: "test-bootstrap-key", IMPORT_KEY: "test-import-key", JOIN_KEY: "mat-khau-cua-hang-gia", GROQ_API_KEY: "test-groq-key", NVIDIA_API_KEY: "test-nvidia-key", AI_DAILY_LIMIT: "5", AI_OFF: "1" },
      },
    }),
  ],
  test: { include: ["test/**/*.test.ts"] },
});
