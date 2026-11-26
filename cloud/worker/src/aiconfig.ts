// Cấu hình AI CHUNG cho mọi máy, lưu trên cloud (chủ dự án 26/11/2026): thứ tự dự phòng, danh sách mô hình, thời gian chờ.
// Đổi ở đây (PUT /admin/ai-config) là mọi máy theo, không cần cài lại app. Gemini chậm (~50 giây) nên để cuối.
export type AiProviderName = "groq" | "gemini" | "nvidia" | "cloudflare";
export const AI_PROVIDERS: AiProviderName[] = ["groq", "gemini", "nvidia", "cloudflare"];

export interface AiConfig { order: AiProviderName[]; models: Record<AiProviderName, string>; timeoutSec: number }

export const DEFAULT_AI_CONFIG: AiConfig = {
  order: ["groq", "nvidia", "cloudflare", "gemini"],
  models: {
    groq: "openai/gpt-oss-120b, qwen/qwen3.8-27b, openai/gpt-oss-20b",
    nvidia: "nvidia/nemotron-3-ultra-550b-a55b, nvidia/nemotron-3.5-lightning-30b-a3b",
    cloudflare: "@cf/openai/gpt-oss-120b, @cf/meta/llama-4-scout-17b-16e-instruct",
    gemini: "gemini-4-hydrogen, gemini-3.8-flash, gemini-3.5-flash-lite",
  },
  timeoutSec: 25,
};

/** Kiểm tra + chuẩn hóa cấu hình gửi lên; lỗi thì trả chuỗi lý do. Nhà cung cấp thiếu trong `order` được nối vào cuối theo mặc định. */
export function parseAiConfig(raw: unknown, base: AiConfig = DEFAULT_AI_CONFIG): AiConfig | string {
  const o = (raw ?? {}) as Record<string, unknown>;
  const order: AiProviderName[] = [];
  if (o.order !== undefined) {
    if (!Array.isArray(o.order)) return "order phải là danh sách.";
    for (const p of o.order) {
      if (!AI_PROVIDERS.includes(p as AiProviderName)) return `Nhà cung cấp không hợp lệ: ${String(p)}`;
      if (!order.includes(p as AiProviderName)) order.push(p as AiProviderName);
    }
  } else order.push(...base.order);
  for (const p of base.order) if (!order.includes(p)) order.push(p);
  const models = { ...base.models };
  if (o.models !== undefined) {
    if (typeof o.models !== "object" || o.models === null) return "models phải là đối tượng.";
    for (const [k, v] of Object.entries(o.models as Record<string, unknown>)) {
      if (!AI_PROVIDERS.includes(k as AiProviderName)) return `Nhà cung cấp không hợp lệ: ${k}`;
      if (typeof v !== "string" || !v.trim() || v.length > 500) return `Danh sách mô hình ${k} không hợp lệ.`;
      models[k as AiProviderName] = v.trim();
    }
  }
  let timeoutSec = base.timeoutSec;
  if (o.timeoutSec !== undefined) {
    const t = Number(o.timeoutSec);
    if (!Number.isFinite(t) || t < 5 || t > 120) return "timeoutSec phải từ 5 đến 120 giây.";
    timeoutSec = Math.round(t);
  }
  return { order, models, timeoutSec };
}
