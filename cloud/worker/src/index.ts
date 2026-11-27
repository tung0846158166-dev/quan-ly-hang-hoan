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
  /** Cloudflare Workers AI gắn thẳng vào Worker (wrangler.jsonc "ai": { "binding": "AI" }), không cần token. */
  AI?: Ai;
  /** "1" = tắt Workers AI (chỉ dùng khi chạy thử cục bộ). */
  AI_OFF?: string;
  /** Số lượt hỏi AI tối đa mỗi ngày cho cả cửa hàng (bảo vệ hạn mức yêu cầu của gói miễn phí). */
  AI_DAILY_LIMIT?: string;
}

const shop = (env: Env) => env.SHOP.get(env.SHOP.idFromName("main"));

/**
 * Trang web thử chạy trên máy (27/11/2026): trình duyệt chỉ gọi được cloud khi cloud cho phép nguồn đó (CORS).
 * Chỉ mở cho trang cục bộ trên chính máy (localhost / 127.0.0.1); mọi nguồn khác giữ nguyên như cũ (trình duyệt chặn).
 * Vẫn bắt buộc token thiết bị như mọi máy khác.
 */
export function corsOrigin(origin: string | null): string | null {
  if (!origin) return null;
  return /^http:\/\/(localhost|127\.0\.0\.1):\d{2,5}$/.test(origin) ? origin : null;
}

function withCors(res: Response, origin: string): Response {
  if (res.status === 101) return res; // WebSocket: giữ nguyên
  const out = new Response(res.body, res);
  out.headers.set("Access-Control-Allow-Origin", origin);
  out.headers.append("Vary", "Origin");
  return out;
}

export default {
  /** Mọi yêu cầu đi vào MỘT Durable Object duy nhất (một cửa hàng): ghi tuần tự nên quét nhận nguyên tử. */
  async fetch(req: Request, env: Env): Promise<Response> {
    const origin = corsOrigin(req.headers.get("Origin"));
    if (req.method === "OPTIONS") {
      if (!origin) return new Response(null, { status: 204 });
      return new Response(null, { status: 204, headers: {
        "Access-Control-Allow-Origin": origin, "Vary": "Origin",
        "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
        "Access-Control-Allow-Headers": "Authorization, Content-Type",
        "Access-Control-Max-Age": "86400",
      } });
    }
    const res = await shop(env).fetch(req);
    return origin ? withCors(res, origin) : res;
  },

  /**
   * Cron: "17 20 * * *" = dọn dẹp hằng ngày (ảnh quá 90 ngày, nhật ký cũ);
   * còn lại = gợi ý AI theo khung giờ làm việc (giờ Việt Nam 8:00, 10:30, 13:30, 16:30, 19:00, 21:00).
   */
  async scheduled(event: ScheduledController, env: Env): Promise<void> {
    if (event.cron === "17 20 * * *") await shop(env).cleanup();
    else await shop(env).runDigest();
  },
} satisfies ExportedHandler<Env>;
