import { ShopDO } from "./shop";

export { ShopDO };

export interface Env {
  SHOP: DurableObjectNamespace<ShopDO>;
  IMAGES: R2Bucket;
  /** Khóa tạo thiết bị chủ đầu tiên (đặt bằng `wrangler secret put BOOTSTRAP_KEY`). */
  BOOTSTRAP_KEY: string;
  /** Mật khẩu cửa hàng để điện thoại tự ghép (`wrangler secret put JOIN_KEY`, tối thiểu 8 ký tự); không đặt = tắt cách ghép này. */
  JOIN_KEY?: string;
  /** Khóa riêng cho Apps Script đẩy đơn (`wrangler secret put IMPORT_KEY`). */
  IMPORT_KEY: string;
  PHOTO_RETENTION_DAYS: string;
  /** Khóa AI (đặt bằng `wrangler secret put GROQ_API_KEY` / `GEMINI_API_KEY`); không có thì cổng AI báo chưa cấu hình. */
  GROQ_API_KEY?: string;
  GEMINI_API_KEY?: string;
  /** Khóa NVIDIA NIM (build.nvidia.com), tuyến AI dự phòng cuối: `wrangler secret put NVIDIA_API_KEY`. */
  NVIDIA_API_KEY?: string;
  /** Cloudflare Workers AI (cổng chuẩn OpenAI): mã tài khoản + token có quyền Workers AI (`wrangler secret put CF_ACCOUNT_ID` / `CF_AI_TOKEN`). */
  CF_ACCOUNT_ID?: string;
  CF_AI_TOKEN?: string;
  /** Số lượt hỏi AI tối đa mỗi ngày cho cả cửa hàng (bảo vệ hạn mức yêu cầu của gói miễn phí). */
  AI_DAILY_LIMIT?: string;
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
