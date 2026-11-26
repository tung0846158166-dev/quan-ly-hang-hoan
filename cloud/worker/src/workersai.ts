// Cloudflare Workers AI gắn thẳng vào Worker (binding `AI`, không cần token): tuyến AI dự phòng cuối (chủ dự án 26/11/2026).
// Máy khách gọi theo chuẩn OpenAI như mọi nhà cung cấp khác; ở đây đổi yêu cầu sang dạng của Workers AI và đổi kết quả về chuẩn OpenAI.
// Lịch sử gọi công cụ (assistant.tool_calls + tin "tool") được viết lại thành chữ thường để mọi mô hình Workers AI đều hiểu.

type Json = Record<string, unknown>;

export interface WorkersAiCall { model: string; input: Json }

const str = (v: unknown): string => (typeof v === "string" ? v : v == null ? "" : JSON.stringify(v));

/** Yêu cầu chuẩn OpenAI -> lời gọi `env.AI.run(model, input)`. */
export function toWorkersAi(body: Json): WorkersAiCall {
  const model = typeof body.model === "string" && body.model.trim() ? body.model.trim() : "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
  const messages: { role: string; content: string }[] = [];
  for (const raw of Array.isArray(body.messages) ? body.messages : []) {
    const m = (raw ?? {}) as Json;
    const role = str(m.role);
    if (role === "tool") {
      messages.push({ role: "user", content: `Kết quả công cụ ${str(m.name) || ""}`.trim() + ":\n" + str(m.content) });
      continue;
    }
    const calls = Array.isArray(m.tool_calls) ? m.tool_calls as Json[] : [];
    if (role === "assistant" && calls.length > 0) {
      const text = calls.map((c) => {
        const f = (c.function ?? {}) as Json;
        return `Gọi công cụ ${str(f.name)} với tham số ${str(f.arguments) || "{}"}`;
      }).join("\n");
      messages.push({ role: "assistant", content: [str(m.content), text].filter(Boolean).join("\n") });
      continue;
    }
    messages.push({ role: role === "system" || role === "assistant" ? role : "user", content: str(m.content) });
  }
  const input: Json = { messages };
  const tools = Array.isArray(body.tools) ? (body.tools as Json[]).map((t) => {
    const f = (t.function ?? t) as Json;
    return { name: str(f.name), description: str(f.description), parameters: f.parameters ?? { type: "object", properties: {} } };
  }).filter((t) => t.name) : [];
  if (tools.length > 0 && body.tool_choice !== "none") input.tools = tools;
  if (typeof body.temperature === "number") input.temperature = body.temperature;
  if (typeof body.max_tokens === "number") input.max_tokens = body.max_tokens;
  return { model, input };
}

/** Kết quả `env.AI.run` -> phản hồi chuẩn OpenAI (chat.completion). Mô hình đã trả chuẩn OpenAI thì giữ nguyên. */
export function fromWorkersAi(model: string, out: unknown): Json {
  const o = (out ?? {}) as Json;
  if (Array.isArray(o.choices)) return o;
  const calls = Array.isArray(o.tool_calls) ? o.tool_calls as Json[] : [];
  const toolCalls = calls.map((c, i) => {
    const f = (c.function ?? c) as Json;
    const args = f.arguments;
    return { id: `call_cf_${i}`, type: "function", function: { name: str(f.name), arguments: typeof args === "string" ? args : JSON.stringify(args ?? {}) } };
  }).filter((c) => c.function.name);
  const text = typeof o.response === "string" ? o.response : o.response == null ? "" : JSON.stringify(o.response);
  return {
    id: "cf-" + Date.now().toString(36), object: "chat.completion", model,
    choices: [{
      index: 0, finish_reason: toolCalls.length > 0 ? "tool_calls" : "stop",
      message: { role: "assistant", content: toolCalls.length > 0 && !text ? null : text, ...(toolCalls.length > 0 ? { tool_calls: toolCalls } : {}) },
    }],
    ...(o.usage ? { usage: o.usage } : {}),
  };
}

/** Lỗi của Workers AI -> mã HTTP để máy khách tự chuyển mô hình (429 = hết hạn mức/quá tải, 404 = sai tên mô hình). */
export function workersAiErrorStatus(e: unknown): number {
  const m = String((e as Error)?.message ?? e).toLowerCase();
  if (/neuron|limit|quota|capacity|rate|too many|overload|busy/.test(m)) return 429;
  if (/no such model|model not found|not found|invalid model|deprecated|end of life|5007|5028/.test(m)) return 404;
  return 502;
}
