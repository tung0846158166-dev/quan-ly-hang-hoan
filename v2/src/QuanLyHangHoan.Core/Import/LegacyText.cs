using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Core.Import;

/// <summary>
/// Các hàm xử lý chữ/mã/số/ngày của app cũ, chuyển sang C# GIỮ NGUYÊN hành vi (đối chiếu bằng "đáp án chuẩn" sinh từ mã JS cũ,
/// xem docs/auto-import-plan.md). Mọi \d của JS (chỉ 0-9) được viết [0-9] để không khớp chữ số Unicode khác.
/// </summary>
public static class LegacyText
{
    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const RegexOptions C = RegexOptions.CultureInvariant;

    /// <summary>removeVietnameseTones: bỏ dấu, đ->d, VIẾT THƯỜNG.</summary>
    public static string RemoveTones(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (ch >= '̀' && ch <= 'ͯ') continue;
            sb.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch);
        }
        return sb.ToString().ToLowerInvariant();
    }

    /// <summary>Khóa tên cột: bỏ dấu, thường, chỉ giữ a-z0-9.</summary>
    public static string Key(string? s) => Regex.Replace(RemoveTones(s), "[^a-z0-9]", "", C);

    // ------------------------------------------------------------ mã vận đơn
    public static string CleanTrackingCode(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var str = raw.Trim();

        if (str.Contains("http://") || str.Contains("https://") || str.Contains("spx.vn") || str.Contains("ghn.vn"))
        {
            var mp = Regex.Match(str, @"(?:spx|track|code|c|id|bill|order_code|s)=([A-Za-z0-9\.\-_]+)", I);
            if (mp.Success && mp.Groups[1].Length > 0) str = mp.Groups[1].Value;
        }

        foreach (var pattern in new[]
        {
            @"(SPXVN[0-9A-Za-z]{6,18}|VN2[4-9][0-9A-Za-z]{8,14})",
            @"(SOC[0-9A-Za-z]{6,16})",
            @"((?:NIVN|NLVN|SHPVN)[0-9A-Za-z]{6,16})",
            @"(TTVN[0-9A-Za-z]{6,16})",
            @"(SHOPEEVTP[0-9A-Za-z]{6,18}|VTP?[0-9A-Za-z]{6,15})",
            @"(GHN[0-9A-Za-z]{6,14}|G8[0-9A-Za-z]{6,14}|GY[0-9A-Za-z]{6,10})",
            @"(S[0-9]+[\.\-][0-9A-Za-z\.\-]{6,24})",
        })
        {
            var m = Regex.Match(str, pattern, I);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }

        var mj = Regex.Match(str, @"(?<![0-9])(8[0246][0-9]{10,13})(?![0-9])", C);
        if (mj.Success) return mj.Groups[1].Value;

        var cleaned = Regex.Replace(str, @"^[\*""'<>#\s\[\]\(\)]+|[\*""'<>#\s\[\]\(\)]+$", "", C).Trim();
        if (cleaned.Contains('/') || cleaned.Contains('|') || cleaned.Contains(';'))
            foreach (var p in Regex.Split(cleaned, @"[\/\|;]", C))
            {
                var pc = CleanTrackingCode(p);
                if (pc.Length > 0) return pc;
            }

        return cleaned.ToUpperInvariant();
    }

    /// <summary>normalizeScientificOrderId: mã đơn bị Excel đổi sang số khoa học (5.87E+17) được khôi phục.</summary>
    public static string NormalizeScientificOrderId(string? val)
    {
        if (val is null) return "";
        var s = Regex.Replace(val.Trim(), @"^['""]+|['""]+$", "", C).Trim();
        if (s.Length == 0) return "";
        var m = Regex.Match(s, @"^([+-]?[0-9]+)(?:\.([0-9]+))?[eE]\+?([0-9]+)$", C);
        if (m.Success && Regex.IsMatch(s, @"[.+]", C) && int.TryParse(m.Groups[3].Value, out var exp) && exp <= 40)
        {
            var intPart = m.Groups[1].Value.TrimStart('+', '-');
            var frac = m.Groups[2].Success ? m.Groups[2].Value : "";
            return exp >= frac.Length
                ? intPart + frac + new string('0', exp - frac.Length)
                : intPart + frac[..exp];
        }
        return s;
    }

    // ------------------------------------------------------------ số / tiền
    /// <summary>parseImportedQuantity: số nguyên không âm, ném FormatException nếu sai.</summary>
    public static int ParseQuantity(string? value, int fallback = 0)
    {
        if (value is null || value.Trim().Length == 0) return fallback;
        var text = Regex.Replace(value.Trim(), @"\s", "", C);
        if (Regex.IsMatch(text, @"^[0-9]+[.,]0{1,2}$", C)) text = Regex.Split(text, @"[.,]", C)[0];
        else if (Regex.IsMatch(text, @"^[0-9]{1,3}(?:,[0-9]{3})+$", C)) text = text.Replace(",", "");
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || q != Math.Floor(q) || q < 0 || q > 9007199254740991d)
            throw new FormatException("Số lượng không hợp lệ: " + value + ". Cần số nguyên không âm.");
        return checked((int)q);
    }

    /// <summary>parseMoneyCellValue: "138580.00" -> 138580; "138.580" (dấu nghìn) -> 138580.</summary>
    public static long ParseMoney(string? val)
    {
        var s = (val ?? "").Trim();
        if (s.Length == 0) return 0;
        s = Regex.Replace(s, @"[.,][0-9]{1,2}$", "", C);
        var digits = Regex.Replace(s, @"[^0-9]", "", C);
        return digits.Length > 0 && long.TryParse(digits, out var n) ? n : 0;
    }

    // ------------------------------------------------------------ ngày
    /// <summary>normalizeShortDate (năm hiện tại truyền vào để test cố định).</summary>
    public static string NormalizeShortDate(string? val, int currentYear)
    {
        var s = (val ?? "").Trim();
        if (s.Length == 0) return "";
        var timeSuffix = "";
        var datePart = s;
        var mt = Regex.Match(s, @"\s+([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)$", C);
        if (mt.Success) { timeSuffix = " " + mt.Groups[1].Value; datePart = s[..mt.Index].Trim(); }

        var mIso = Regex.Match(datePart, @"^([0-9]{4})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{1,2})", C);
        if (mIso.Success) return $"{mIso.Groups[3].Value.PadLeft(2, '0')}/{mIso.Groups[2].Value.PadLeft(2, '0')}/{mIso.Groups[1].Value}{timeSuffix}";

        var mShort = Regex.Match(datePart, @"^([0-9]{1,2})[\/\-\.]([0-9]{1,2})$", C);
        if (mShort.Success) return $"{mShort.Groups[1].Value.PadLeft(2, '0')}/{mShort.Groups[2].Value.PadLeft(2, '0')}/{currentYear}{timeSuffix}";

        var mFull = Regex.Match(datePart, @"^([0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{2,4})$", C);
        if (mFull.Success)
        {
            var y = mFull.Groups[3].Value;
            if (y.Length == 2) y = "20" + y;
            return $"{mFull.Groups[1].Value.PadLeft(2, '0')}/{mFull.Groups[2].Value.PadLeft(2, '0')}/{y}{timeSuffix}";
        }
        return s;
    }

    /// <summary>extractDateFromCellValue (nhánh chuỗi): ra "dd/MM/yyyy [HH:mm[:ss]]".</summary>
    public static string ExtractDate(string? val, int currentYear)
    {
        var s = (val ?? "").Trim();
        if (s.Length == 0) return "";

        var timeStr = "";
        var mTime = Regex.Match(s, @"(?:\s|^)([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)(?:\s|$)", C);
        if (mTime.Success)
        {
            var p = mTime.Groups[1].Value.Split(':');
            var ss = p.Length > 2 ? p[2].PadLeft(2, '0') : "";
            timeStr = $"{p[0].PadLeft(2, '0')}:{p[1].PadLeft(2, '0')}{(ss.Length > 0 && ss != "00" ? ":" + ss : "")}";
        }
        string Suffix() => timeStr.Length > 0 ? " " + timeStr : "";

        var dmy = Regex.Match(s, @"(\b[0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{4})\b", C);
        if (dmy.Success) return $"{dmy.Groups[1].Value.PadLeft(2, '0')}/{dmy.Groups[2].Value.PadLeft(2, '0')}/{dmy.Groups[3].Value}{Suffix()}";

        var iso = Regex.Match(s, @"(\b[0-9]{4})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{1,2})\b", C);
        if (iso.Success) return $"{iso.Groups[3].Value.PadLeft(2, '0')}/{iso.Groups[2].Value.PadLeft(2, '0')}/{iso.Groups[1].Value}{Suffix()}";

        var sh = Regex.Match(s, @"(\b[0-9]{1,2})[\/\-\.]([0-9]{1,2})[\/\-\.]([0-9]{2})\b", C);
        if (sh.Success) return $"{sh.Groups[1].Value.PadLeft(2, '0')}/{sh.Groups[2].Value.PadLeft(2, '0')}/20{sh.Groups[3].Value}{Suffix()}";

        return NormalizeShortDate(s, currentYear);
    }

    // ------------------------------------------------------------ hãng vận chuyển
    /// <summary>detectCarrierByTrackingCode (hàm toàn cục của app cũ); null nếu không nhận ra.</summary>
    public static string? DetectCarrierByTrackingCode(string? rawTrack)
    {
        var code = (rawTrack ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return null;

        if (code.StartsWith("SPXVN") || code.StartsWith("SPX") || Regex.IsMatch(code, @"^VN2[4-9][0-9A-Z]{8,14}$", C)) return "SPX Express";
        if (code.Contains('.') || code.StartsWith("GHTK")) return "GHTK";
        if (!code.StartsWith("SHOPEEVTP") && (code.StartsWith("NIVN") || code.StartsWith("SHP") || code.StartsWith("NVSG") || code.StartsWith("NVVN"))) return "Ninja Van";
        if (code.StartsWith("AHA")) return "AhaMove";
        if (Regex.IsMatch(code, @"^[A-Z]{2}[0-9]{9}VN$", C) || (code.EndsWith("VN") && code.Length == 13)) return "VNPost";
        if (code.StartsWith("SHOPEEVTP") || code.StartsWith("VTP") || code.StartsWith("VT")) return "Viettel Post";
        if (code.StartsWith("TTVN") || code.StartsWith("BEST")) return "Best Express";
        if (Regex.IsMatch(code, @"^01M[0-9A-Z]{20,25}$", C)) return "Khác";
        if (Regex.IsMatch(code, @"^[0-9]+$", C))
        {
            if (code.Length == 12 && (code.StartsWith("80") || code.StartsWith("82") || code.StartsWith("84") || code.StartsWith("86") || code.StartsWith("66"))) return "J&T Express";
            if (code.Length >= 13 && code.Length <= 14) return "Best Express";
            if (code.Length == 12) return "Best Express";
            if (code.Length >= 9 && code.Length <= 11) return "Viettel Post";
        }
        if (code.StartsWith("GHN") || Regex.IsMatch(code, @"^GY[A-Z0-9]{6,8}$", C) ||
            (Regex.IsMatch(code, @"^[A-Z0-9]{6,10}$", C) && Regex.IsMatch(code, @"[A-Z]", C) && Regex.IsMatch(code, @"[0-9]", C))) return "GHN";
        return null;
    }

    /// <summary>detectCarrierFromCode: hàm cục bộ trong bộ nạp TikTok (khác hàm toàn cục ở trên, được thử trước).</summary>
    public static string DetectTikTokCarrierFromCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var t = code.ToUpperInvariant().Trim();
        if (Regex.IsMatch(t, @"^8[0245][0-9]{10,12}$", C)) return "J&T Express";
        if (t.StartsWith("VTP") || t.StartsWith("VT")) return "Viettel Post";
        if (t.StartsWith("SPX")) return "SPX Express";
        if (t.StartsWith("GHN") || t.StartsWith("G8")) return "GHN";
        if (t.StartsWith('S') && t.Contains('.')) return "GHTK";
        if (t.StartsWith("NLVN") || t.StartsWith("NIVN") || t.StartsWith("SHPVN")) return "Ninja Van";
        if (t.StartsWith("VN") || t.EndsWith("VN")) return "VNPost";
        if (t.StartsWith("BEST") || t.StartsWith("TTVN")) return "Best Express";
        return "";
    }

    // ------------------------------------------------------------ ghi chú khách
    private static readonly string[] BuyerNoteDirect =
    {
        "Ghi chú của Người mua khi trả hàng", "Nhận xét từ Người mua", "Ghi chú của Người mua khi đặt hàng", "Ghi chú của khách",
        "Lưu ý từ người mua", "Ghi chú của người mua", "Ghi chú người mua", "Ghi chú khách hàng", "Lời nhắn từ người mua",
        "Buyer Message", "Customer Note", "Buyer Note",
    };

    public static bool IsNoneNote(string? v) => Regex.IsMatch(RemoveTones(v), @"^(không|khong|none|\-)$", I);

    /// <summary>extractBuyerNote(r)</summary>
    public static string ExtractBuyerNote(IReadOnlyDictionary<string, string> r)
    {
        foreach (var dl in BuyerNoteDirect)
            if (r.TryGetValue(dl, out var raw))
            {
                var v = (raw ?? "").Trim();
                if (v.Length > 0 && !IsNoneNote(v)) return v;
            }
        foreach (var (k, raw) in r)
        {
            var kn = Key(k);
            var hit = (kn.Contains("nhanxet") && kn.Contains("nguoimua"))
                      || (kn.Contains("ghichu") && (kn.Contains("nguoimua") || kn.Contains("khach")))
                      || kn is "buyermessage" or "customernote" or "buyernote";
            if (!hit) continue;
            if (kn.Contains("nguoiban") || kn.Contains("tien") || kn.Contains("phi") || kn.Contains("gia") || kn.Contains("lydo") || kn.Contains("lido")) continue;
            var v = (raw ?? "").Trim();
            if (v.Length > 0 && !IsNoneNote(v)) return v;
        }
        return "";
    }

    // ------------------------------------------------------------ sản phẩm / màu / size
    private static readonly string[] ColorWords =
    {
        "xanh navy", "xanh den", "xanh duong", "xanh la", "xanh reu", "xanh ngoc", "xanh coban", "xanh bo", "xanh nhat", "xanh dam", "xanh pastel", "xanh lam",
        "xanh than", "xanh nau", "xanh kem", "xam than", "xam dam", "xam nhat", "mau mo", "kaki", "retro", "mori", "urban", "koda",
        "den", "trang", "trang nga", "trang tinh", "do do", "do tuoi", "do man", "do ruou", "hong", "hong phan", "hong baby",
        "vang", "vang bo", "vang chanh", "nau", "nau dat", "nau bo", "nau tay", "nau socola",
        "be", "be nhat", "be dam", "kem", "kem sua", "xam", "xam tieu", "xam chuot", "xam khoi", "xam ghi", "ghi", "tim", "tim than", "cam", "cam dat", "reu",
    };

    private static readonly string[] ReasonKeywords =
    {
        "khong nhan", "nguoi nhan", "tu choi", "that bai", "khong thanh", "huy don", "hoan tien", "tra hang", "ly do", "be vo", "hu hong",
        "thieu hang", "doi y", "chua nhan", "lien lac", "dia chi", "sdt", "nghe may", "hen giao", "bom hang", "khong mua", "sai hang",
        "kem chat luong", "khac mo ta", "khong dung", "dong kiem", "thu tien", "chuyen hoan", "luu kho", "buyer cancelled", "delivery failed",
        "rejected", "return reason", "package lost",
    };

    public static bool IsReturnReasonText(string? str)
    {
        var s = (str ?? "").Trim();
        if (s.Length == 0) return false;
        var norm = RemoveTones(s);
        if (ReasonKeywords.Any(norm.Contains)) return true;
        return s.Length > 30;
    }

    public static bool IsKnownColorName(string? str)
    {
        var norm = RemoveTones((str ?? "").Trim());
        if (norm.Length == 0) return false;
        return ColorWords.Any(c => norm == c || norm.StartsWith(c + " ") || norm.EndsWith(" " + c));
    }

    public static bool IsValidColorString(string? str)
    {
        var s = (str ?? "").Trim();
        if (s.Length == 0) return false;
        if (Regex.IsMatch(s, @"^[0-9]{4,}$", C)) return false;
        if (IsReturnReasonText(s)) return false;
        if (s.Length > 25) return false;
        return !Regex.IsMatch(RemoveTones(s), @"^(xxxl|xxl|xl|xs|s|m|l|[2-5]xl|[0-9]{2}|freesize|free size|onesize|one size)$", I);
    }

    public static bool IsValidSizeString(string? str)
    {
        var s = (str ?? "").Trim();
        if (s.Length == 0) return false;
        if (IsReturnReasonText(s)) return false;
        if (Regex.IsMatch(s, @"^[0-9]{4,}$", C)) return false;
        var norm = RemoveTones(s);
        if (norm is "freesize" or "free size" or "onesize" or "one size" or "fs") return true;
        if (s.Length > 10) return false;
        if (Regex.Split(s, @"\s+", C).Count(w => w.Length > 0) > 2) return false;
        if (IsKnownColorName(s)) return false;
        return true;
    }

    private static string StripQualitySuffix(string? str)
    {
        var s = str ?? "";
        s = Regex.Replace(s, @"\s*[\-\|\/]\s*premium\s*cao\s*c[aấ]p?\b", "", I);
        s = Regex.Replace(s, @"\s*[\-\|\/]\s*cao\s*c[aấ]p?\b", "", I);
        s = Regex.Replace(s, @"\s*[\-\|\/]\s*premium\b", "", I);
        s = Regex.Replace(s, @"\(\s*(?:pre\s*)?cao\s*c[aấ]p?\s*\)", "", I);
        s = Regex.Replace(s, @"\(\s*premium\s*\)", "", I);
        s = Regex.Replace(s, @"\bpremium\s*cao\s*c[aấ]p?\b", "", I);
        s = Regex.Replace(s, @"\s+premium$", "", I);
        s = s.Trim();
        s = Regex.Replace(s, @"^[\-\,\;\/\|]+|[\-\,\;\/\|]+$", "", C);
        return s.Trim();
    }

    public sealed record ProductVariation(string Product, string Color, string Size, string ExtraNote);

    /// <summary>parseSaleworkProductAndVariation</summary>
    public static ProductVariation ParseProductAndVariation(string? rawProd, string? rawVar, string? rawSellerSku)
    {
        var cleanProd = (rawProd ?? "").Trim();
        var cleanVar = (rawVar ?? "").Trim();
        var cleanSku = (rawSellerSku ?? "").Trim();

        var combined = $"{cleanProd} {cleanVar}";
        var premium = "";
        if (Regex.IsMatch(combined, @"premium\s*cao\s*c[aấ]p?", I)) premium = "Premium Cao Cấp";
        else if (Regex.IsMatch(combined, @"\(\s*premium\s*\)", I)) premium = "Premium Cao Cấp";
        else if (Regex.IsMatch(combined, @"[\-\|\/]\s*premium\b", I) || Regex.IsMatch(cleanVar, @"\s+premium$", I)) premium = "Premium Cao Cấp";

        if (cleanProd.Length > 0 && cleanVar.Length > 0)
        {
            var varNoSuffix = StripQualitySuffix(cleanVar);
            var lowerProd = cleanProd.ToLowerInvariant();
            foreach (var candidate in new[] { cleanVar.ToLowerInvariant(), varNoSuffix.ToLowerInvariant() })
            {
                if (candidate.Length == 0) continue;
                var dash = " - " + candidate;
                if (lowerProd.EndsWith(dash, StringComparison.Ordinal))
                {
                    cleanProd = cleanProd[..(cleanProd.Length - dash.Length)].Trim();
                    break;
                }
            }
        }

        cleanProd = Regex.Replace(cleanProd, @"\s*[\-\|\/]\s*premium\s*cao\s*c[aấ]p?\s*$", "", I);
        cleanProd = Regex.Replace(cleanProd, @"\s*\(\s*premium\s*\)\s*$", "", I).Trim();

        var mTrail = Regex.Match(cleanProd, @"^(.*?)\s+-\s+([^,\-]{1,35},\s*(?:XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size))$", I);
        if (mTrail.Success)
        {
            cleanProd = mTrail.Groups[1].Value.Trim();
            if (cleanVar.Length == 0) cleanVar = mTrail.Groups[2].Value.Trim();
        }

        if (premium.Length > 0 && cleanProd.Length > 0 && !Regex.IsMatch(cleanProd, "premium", I))
            cleanProd = $"{cleanProd} - {premium}";

        var cleanedVar = StripQualitySuffix(cleanVar);
        string color = "", size = "", extra = "";

        if (cleanedVar.Length > 0 && !Regex.IsMatch(cleanedVar, @"^[0-9]{4,}$", C) && !Regex.IsMatch(cleanedVar, @"^[0-9]+\.[0-9]+e\+[0-9]+$", I))
        {
            var mColor = Regex.Match(cleanedVar, @"(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*([^,;\/\n\|]+)", I);
            var mSize = Regex.Match(cleanedVar, @"(?:kích\s*thước|kích\s*cỡ|size|kich\s*thuoc|kich\s*co)\s*[:\-]\s*([^,;\/\n\|]+)", I);
            if (mColor.Success && IsValidColorString(mColor.Groups[1].Value)) color = StripQualitySuffix(mColor.Groups[1].Value);
            if (mSize.Success && IsValidSizeString(mSize.Groups[1].Value)) size = mSize.Groups[1].Value.Trim().ToUpperInvariant();

            if (color.Length == 0 || size.Length == 0)
            {
                const string sizeExact = @"^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2}|Free\s*size|Freesize|One\s*size|[<>]\s*[0-9]+\s*kg)$";
                var parts = Regex.Split(cleanedVar, @"[,;\/\|]", C).Select(StripQualitySuffix).Where(x => x.Length > 0).ToList();
                foreach (var p in parts)
                {
                    foreach (var sp in Regex.Split(p, @"\s*\-\s*", C).Select(StripQualitySuffix).Where(x => x.Length > 0))
                    {
                        if (Regex.IsMatch(sp, sizeExact, I) && size.Length == 0 && IsValidSizeString(sp))
                            size = Regex.Replace(sp.ToUpperInvariant(), @"\s+", "", C);
                        else if (color.Length == 0 && IsValidColorString(sp))
                            color = Regex.Replace(sp, @"^(?:màu\s*sắc|màu|mau\s*sac|mau|color)\s*[:\-]\s*", "", I).Trim();
                        else if (IsReturnReasonText(sp) && extra.Length == 0)
                            extra = sp;
                    }
                }
                if (color.Length == 0 && parts.Count > 0 && IsValidColorString(parts[0]) && !Regex.IsMatch(parts[0], sizeExact, I))
                    color = parts[0];
            }
        }

        if (color.Length == 0 && cleanProd.Length > 0)
        {
            var withoutPrem = StripQualitySuffix(cleanProd);
            var normProd = RemoveTones(withoutPrem);
            foreach (var cWord in ColorWords.OrderByDescending(c => c.Length))
                if (normProd.EndsWith(" " + cWord, StringComparison.Ordinal))
                {
                    color = withoutPrem[(withoutPrem.Length - cWord.Length)..].Trim();
                    break;
                }
        }

        if ((color.Length == 0 || size.Length == 0) && cleanSku.Length > 0 && cleanSku.Contains('_'))
        {
            var skuParts = cleanSku.Split('_').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (skuParts.Count >= 2)
            {
                var last = skuParts[^1];
                if (Regex.IsMatch(last, @"^(XXXL|XXL|XL|XS|S|M|L|[2-5]XL|[0-9]{2})$", I) && size.Length == 0) size = last.ToUpperInvariant();
                if (skuParts.Count >= 3 && color.Length == 0) color = string.Join(" ", skuParts.Skip(1).Take(skuParts.Count - 2));
                if (cleanProd.Length == 0 && skuParts[0].Length > 0) cleanProd = skuParts[0];
            }
        }

        return new ProductVariation(cleanProd, color, size, extra);
    }
}
