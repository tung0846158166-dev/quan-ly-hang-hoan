import { describe, expect, it } from "vitest";
import { fromWorkersAi, toWorkersAi, workersAiErrorStatus } from "../src/workersai";

// Workers AI gắn thẳng vào Worker: đổi chuẩn OpenAI <-> Workers AI. Dữ liệu GIẢ.
describe("Workers AI (binding AI trong Worker)", () => {
  it("đổi yêu cầu OpenAI: giữ hội thoại, công cụ thành dạng Workers AI, lịch sử gọi công cụ thành chữ", () => {
    const c = toWorkersAi({
      model: "@cf/meta/llama-3.3-70b-instruct-fp8-fast", temperature: 0.2,
      messages: [
        { role: "system", content: "Bạn là trợ lý" },
        { role: "user", content: "Hôm nay có bao nhiêu đơn?" },
        { role: "assistant", content: null, tool_calls: [{ id: "a", type: "function", function: { name: "count_orders", arguments: "{\"day\":\"today\"}" } }] },
        { role: "tool", tool_call_id: "a", name: "count_orders", content: "{\"n\":12}" },
      ],
      tools: [{ type: "function", function: { name: "count_orders", description: "Đếm đơn", parameters: { type: "object", properties: { day: { type: "string" } } } } }],
    });
    expect(c.model).toBe("@cf/meta/llama-3.3-70b-instruct-fp8-fast");
    const msgs = c.input.messages as { role: string; content: string }[];
    expect(msgs.map((m) => m.role)).toEqual(["system", "user", "assistant", "user"]);
    expect(msgs[2]!.content).toContain("count_orders");
    expect(msgs[3]!.content).toContain("{\"n\":12}");
    expect(c.input.tools).toEqual([{ name: "count_orders", description: "Đếm đơn", parameters: { type: "object", properties: { day: { type: "string" } } } }]);
    expect(c.input.temperature).toBe(0.2);
  });

  it("đổi kết quả về chuẩn OpenAI: câu trả lời thường và lời gọi công cụ", () => {
    const a = fromWorkersAi("m", { response: "Có 12 đơn." }) as any;
    expect(a.choices[0].message).toEqual({ role: "assistant", content: "Có 12 đơn." });
    expect(a.choices[0].finish_reason).toBe("stop");
    const b = fromWorkersAi("m", { response: null, tool_calls: [{ name: "count_orders", arguments: { day: "today" } }] }) as any;
    expect(b.choices[0].finish_reason).toBe("tool_calls");
    expect(b.choices[0].message.tool_calls[0]).toEqual({ id: "call_cf_0", type: "function", function: { name: "count_orders", arguments: "{\"day\":\"today\"}" } });
    const passthrough = { choices: [{ message: { role: "assistant", content: "ok" } }] };
    expect(fromWorkersAi("m", passthrough)).toBe(passthrough);
  });

  it("lỗi hết hạn mức / quá tải -> 429, sai mô hình -> 404 để máy khách tự chuyển mô hình", () => {
    expect(workersAiErrorStatus(new Error("You have used up your daily free allocation of 10,000 neurons"))).toBe(429);
    expect(workersAiErrorStatus(new Error("Capacity temporarily exceeded"))).toBe(429);
    expect(workersAiErrorStatus(new Error("No such model @cf/x/y"))).toBe(404);
    expect(workersAiErrorStatus(new Error("boom"))).toBe(502);
  });
});
