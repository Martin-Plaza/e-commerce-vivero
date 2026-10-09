import { defineConfig } from "@neon/config/v1";

export default defineConfig({
  buckets: {
    "gymshop-product-images": { access: "public_read" },
  },
});
