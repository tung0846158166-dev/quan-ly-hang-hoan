import Foundation

/// Kênh ntfy.sh: POST bản tin, nhận phản hồi bằng SSE, và hỏi lại (poll) khi cần.
public final class NtfyClient {
    private let session: URLSession
    public init(session: URLSession = .shared) { self.session = session }

    public struct HTTPError: Error { public let status: Int }

    private func buildURL(_ topic: String, _ tail: String) throws -> URL {
        guard !topic.isEmpty, let u = URL(string: "https://ntfy.sh/\(topic)\(tail)") else {
            throw URLError(.badURL)
        }
        return u
    }

    public func post(topic: String, title: String, body: String) async throws {
        let encodedTitle = title.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "Shopee"
        var req = URLRequest(url: try buildURL(topic, "?title=\(encodedTitle)"))
        req.httpMethod = "POST"
        req.httpBody = Data(body.utf8)
        req.timeoutInterval = 20
        let (_, resp) = try await session.data(for: req)
        if let h = resp as? HTTPURLResponse, !(200..<300).contains(h.statusCode) { throw HTTPError(status: h.statusCode) }
    }

    /// Mỗi phần tử là nội dung sau "data:" của một sự kiện SSE (kể cả sự kiện open).
    public func events(topic: String) -> AsyncThrowingStream<String, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    var req = URLRequest(url: try self.buildURL(topic, "/sse"))
                    req.timeoutInterval = 120
                    req.setValue("text/event-stream", forHTTPHeaderField: "Accept")
                    let (bytes, resp) = try await self.session.bytes(for: req)
                    if let h = resp as? HTTPURLResponse, !(200..<300).contains(h.statusCode) { throw HTTPError(status: h.statusCode) }
                    for try await line in bytes.lines {
                        if line.hasPrefix("data:") {
                            continuation.yield(String(line.dropFirst(5)).trimmingCharacters(in: .whitespaces))
                        }
                    }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    /// Lấy lại các bản tin gần đây (vd since = "10m") để bắt phản hồi bị lỡ khi mất kết nối.
    public func poll(topic: String, since: String) async throws -> [String] {
        var req = URLRequest(url: try buildURL(topic, "/json?poll=1&since=\(since)"))
        req.timeoutInterval = 20
        let (data, _) = try await session.data(for: req)
        return String(decoding: data, as: UTF8.self).split(separator: "\n").map(String.init)
    }
}
