using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Mã chạy trong trang Salework (port 1:1 từ salework_auto.js) và các hàm kiểm tra an toàn URL / văn bản nút.
/// </summary>
public static class SaleworkPageScript
{
    /// <summary>
    /// Tuyệt đối không bấm phần tử có chữ "cập nhật" (bắt Salework đồng bộ lại TikTok).
    /// </summary>
    public static readonly Regex ForbiddenTextRegex = new(@"c[aậ]p\s*nh[aậ]t", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Từ chối lưu URL đăng nhập.
    /// </summary>
    public static readonly Regex LoginUrlRegex = new(@"login|signin|dang-?nhap", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string PageScriptBody = """
(function (action, arg) {
  const norm = s => String(s || '').normalize('NFC').replace(/\s+/g, ' ').trim().toLowerCase();
  const visible = el => !!(el.offsetWidth || el.offsetHeight || (el.getClientRects && el.getClientRects().length));
  const FORBID = /c[aậ]p\s*nh[aậ]t/i;
  const SEL = 'button,a,[role=tab],[role=button],[role=radio],li,span,label,div,p';
  function find(text, mode, within) {
    const t = norm(text);
    const root = within || document;
    const all = Array.from(root.querySelectorAll(SEL)).filter(el => visible(el));
    const variants = x => [x, x.replace(/^[a-z_]{3,}\s+(?=\S)/, ''), x.replace(/(?<=\S)\s+[a-z_]{3,}$/, '')];
    const hit = all.filter(el => variants(norm(el.textContent)).some(v => mode === 'prefix' ? v.startsWith(t) : v === t));
    const leaf = hit.filter(el => !hit.some(o => o !== el && el.contains(o)));
    leaf.sort((a, b) => norm(a.textContent).length - norm(b.textContent).length);
    return leaf;
  }
  function dialogRoot() {
    const d = Array.from(document.querySelectorAll('[role=dialog],.ant-modal,.modal,.el-dialog,.v-dialog,.MuiDialog-root,.q-dialog'))
      .filter(visible);
    return d.length ? d[d.length - 1] : null;
  }
  function click(el) {
    let target = el;
    const inp = el.querySelector && el.querySelector('input[type=radio]');
    if (inp) target = inp;
    target.scrollIntoView && target.scrollIntoView({ block: 'center' });
    ['mousedown', 'mouseup'].forEach(n => target.dispatchEvent(new MouseEvent(n, { bubbles: true, cancelable: true, view: window })));
    target.click();
  }
  if (action === 'state') {
    const body = norm(document.body ? document.body.innerText : '');
    return {
      url: location.href,
      hasPassword: !!document.querySelector('input[type=password]'),
      hasExport: find('xuất excel', 'exact').length > 0,
      hasTab: find('đang chờ hoàn', 'prefix').length > 0,
      channelTiktok: /k[eê]nh\s*tiktok/.test(body) || /tiktok/.test(body.slice(0, 4000)),
      title: document.title,
      path: location.host + location.pathname.replace(/\d{5,}/g, '#'),
      buttons: Array.from(new Set(Array.from(document.querySelectorAll('button,[role=tab],[role=button],a')).filter(visible)
        .map(el => String(el.textContent || '').replace(/\s+/g, ' ').trim()).filter(t => t && t.length <= 30))).slice(0, 14)
    };
  }
  function locate(a) {
    if (FORBID.test(a.text)) return { error: 'từ chối bấm nút có chữ "cập nhật"' };
    let within = a.inDialog ? dialogRoot() : null;
    let c = find(a.text, a.mode || 'exact', within);
    if (!c.length && within) c = find(a.text, a.mode || 'exact', null);
    if (!c.length) return { error: 'không thấy "' + a.text + '"' };
    if (FORBID.test(c[0].textContent)) return { error: 'phần tử khớp có chữ "cập nhật", bỏ qua' };
    return { el: c[0], count: c.length };
  }
  if (action === 'click') {
    const L = locate(arg);
    if (L.error) return { ok: false, error: L.error };
    click(L.el);
    return { ok: true, count: L.count };
  }
  if (action === 'rect') {
    const L = locate(arg);
    if (L.error) return { ok: false, error: L.error };
    L.el.scrollIntoView && L.el.scrollIntoView({ block: 'center' });
    const r = L.el.getBoundingClientRect();
    const x = Math.round(Math.min(Math.max(r.left + r.width / 2, 1), window.innerWidth - 1));
    const y = Math.round(Math.min(Math.max(r.top + r.height / 2, 1), window.innerHeight - 1));
    if (r.width < 1 || r.height < 1) return { ok: false, error: 'nút không có kích thước' };
    const top = document.elementFromPoint(x, y);
    const topOk = !!top && L.el.contains(top) && !FORBID.test(norm(top.textContent));
    return { ok: true, x, y, topOk, topText: top ? norm(top.textContent).slice(0, 40) : '' };
  }
  if (action === 'hover') {
    const L = locate(arg);
    if (L.error) return { ok: false, error: L.error };
    ['mouseover', 'mouseenter', 'mousemove'].forEach(n => L.el.dispatchEvent(new MouseEvent(n, { bubbles: n !== 'mouseenter', cancelable: true, view: window })));
    return { ok: true };
  }
  if (action === 'exists') { const L = locate(arg); return L.error ? { ok: false, error: L.error } : { ok: true }; }
  if (action === 'texts') {
    const seen = []; const set = new Set();
    Array.from(document.querySelectorAll('button,a,label,span,div,p,li,h1,h2,h3,h4')).filter(visible).forEach(el => {
      if (el.children.length && Array.from(el.children).some(c => norm(c.textContent))) return;
      const t = String(el.textContent || '').replace(/\s+/g, ' ').trim();
      if (t && t.length <= 40 && !/\d{6,}/.test(t) && !set.has(t)) { set.add(t); seen.push(t); }
    });
    return { ok: true, texts: seen.length > 60 ? seen.slice(0, 25).concat(['…'], seen.slice(-35)) : seen };
  }
  if (action === 'mark') {
    const L = locate(arg);
    if (L.error) return { ok: false, error: L.error };
    L.el.scrollIntoView && L.el.scrollIntoView({ block: 'center' });
    L.el.style.outline = '5px solid #ef4444'; L.el.style.outlineOffset = '2px';
    return { ok: true };
  }
  if (action === 'banner') {
    let b = document.getElementById('__app_banner');
    if (!b && document.body) {
      b = document.createElement('div'); b.id = '__app_banner';
      b.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:2147483647;background:#dc2626;color:#fff;font:bold 16px sans-serif;padding:10px;text-align:center;pointer-events:none';
      document.body.appendChild(b);
    }
    if (b) b.textContent = arg.text;
    return { ok: true };
  }
  if (action === 'dialogHas') {
    const d = dialogRoot();
    return { ok: !!d && norm(d.textContent).includes(norm(arg.text)) };
  }
  return { ok: false, error: 'action lạ' };
})
""";

    public static bool IsForbiddenText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = text.Normalize(NormalizationForm.FormC);
        return ForbiddenTextRegex.IsMatch(normalized);
    }

    public static bool IsSafeClickTarget(string? requestedText, string? elementText, string? topElementText = null) =>
        !IsForbiddenText(requestedText) && !IsForbiddenText(elementText) && !IsForbiddenText(topElementText);

    public static bool ValidateOpenUrl(string? url, out string error)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (!trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Địa chỉ phải bắt đầu bằng https://";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool ValidateSaveUrl(string? url, out string error)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (!ValidateOpenUrl(trimmed, out _) || LoginUrlRegex.IsMatch(trimmed))
        {
            error = "Hãy mở trang Đơn hoàn (đã đăng nhập) rồi lưu.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string BuildScript(string action, object? arg = null)
    {
        var actionJson = JsonSerializer.Serialize(action);
        var argJson = arg is null ? "null" : JsonSerializer.Serialize(arg);
        return $"{PageScriptBody.Trim()}({actionJson},{argJson})";
    }
}
