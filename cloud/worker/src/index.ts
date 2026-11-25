import { ShopDO } from "./shop";

export { ShopDO };

export interface Env {
  SHOP: DurableObjectNamespace<ShopDO>;
  IMAGES: R2Bucket;
  /** Khóa tạo thiết bị chủ đầu tiên (đặt bằng `wrangler secret put BOOTSTRAP_KEY`). */
  BOOTSTRAP_KEY: string;
  /** Khóa riêng cho Apps Script đẩy đơn (`wrangler secret put IMPORT_KEY`). */
  IMPORT_KEY: string;
  PHOTO_RETENTION_DAYS: string;
}

const shop = (env: Env) => env.SHOP.get(env.SHOP.idFromName("main"));

export default {
  /** Mọi yêu cầu đi vào MỘT Durable Object duy nhất (một cửa hàng): ghi tuần tự nên quét nhận nguyên tử. */
  fetch(req: Request, env: Env): Promise<Response> {
    return shop(env).fetch(req);
  },

  /** Cron hằng ngày: xóa ảnh quá hạn 90 ngày, dọn nhật ký thao tác cũ. */
  async scheduled(_event: ScheduledController, env: Env): Promise<void> {
    await shop(env).cleanup();
  },
} satisfies ExportedHandler<Env>;
