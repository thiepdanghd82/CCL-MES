namespace CCL.MES.Application.Services;

/// <summary>
/// Gom biến thể nhãn NQ (VI/EN/mã hạng mục) thành một họ, trả cả hai ngôn ngữ
/// tách biệt — UI chọn theo cờ EN/VI, không nhét song ngữ trong một chuỗi.
/// </summary>
public static class IqcParetoLabel
{
    public readonly record struct Family(string Key, string Vi, string En);

    public static Family Classify(string? labelVi, string? labelEn, string? itemKey, string? defectCode)
    {
        var hint = First(itemKey, defectCode) ?? "";
        if (TryFromItemKey(hint, out var fromKey))
            return fromKey;

        var (viClean, extractedEn) = SplitBilingual(labelVi);
        var (enClean, _) = SplitBilingual(labelEn);
        if (TryFromText($"{viClean} {enClean} {hint}", out var fromText))
            return fromText;

        var vi = First(viClean, enClean, hint) ?? "Other";
        var en = First(enClean, extractedEn, viClean, hint) ?? vi;
        return new Family(vi, vi, en);
    }

    /// <summary>Bỏ đuôi <c>(Adhesive)</c> Latin — để một chuỗi một ngôn ngữ.</summary>
    public static (string Text, string? TrailingEnglish) SplitBilingual(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", null);
        var s = raw.Trim();
        var open = s.LastIndexOf('(');
        var close = s.LastIndexOf(')');
        if (open <= 0 || close != s.Length - 1 || close <= open + 1) return (s, null);
        var inner = s[(open + 1)..close].Trim();
        if (inner.Length == 0) return (s, null);
        foreach (var c in inner)
        {
            if (c > 127) return (s, null);
        }
        var outer = s[..open].Trim();
        return string.IsNullOrEmpty(outer) ? (s, null) : (outer, inner);
    }

    private static bool TryFromItemKey(string raw, out Family family)
    {
        family = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var k = raw.Trim().ToUpperInvariant();
        family = k switch
        {
            "RD-01" or "PD-01" or "PD-02" => Fam("wrinkle", "Nhăn/Hằn", "Wrinkle/dent"),
            "RD-02" => Fam("loose", "Xô/Lỏng", "Shifted/loose"),
            "RD-03" => Fam("bleed", "Tràn keo", "Adhesive bleed"),
            "RD-04" or "PD-03" => Fam("blotch", "Loang", "Blotch"),
            "RD-05" or "PD-04" => Fam("scratch", "Xước", "Scratch"),
            "RD-06" or "PD-08" or "TD-02" => Fam("deform", "Biến dạng", "Deformation"),
            "RD-07" or "PD-05" => Fam("colour", "Màu sắc", "Colour"),
            "RD-08" or "PD-06" => Fam("foreign", "Dị vật", "Foreign matter"),
            "RD-09" or "PD-07" or "TD-01" => Fam("dirt", "Bẩn", "Dirt"),
            "RD-10" => Fam("pinhole", "Rỗ/Thủng", "Pinhole"),
            "RD-11" => Fam("misalign", "Lệch", "Misalignment"),
            "RD-12" or "PD-09" or "TD-05" => Fam("burr", "Bavia", "Burr"),
            "RD-13" => Fam("other", "Lỗi khác", "Other"),
            "TD-03" => Fam("damp", "Ẩm ướt", "Damp"),
            "TD-04" => Fam("rust", "Rỉ sét, đổi màu", "Rust / discolouration"),
            "BD-01" or "BD" => Fam("adhesion", "Độ bám dính keo", "Adhesive"),
            _ when k.Contains("DIM", StringComparison.Ordinal) && k.Contains("OOT", StringComparison.Ordinal)
                => Fam("dim_oot", "DIM-OOT", "DIM-OOT"),
            _ => default,
        };
        return family.Key is { Length: > 0 };
    }

    private static bool TryFromText(string blob, out Family family)
    {
        family = default;
        if (Contains(blob, "Nhăn") || Contains(blob, "Hằn") || Contains(blob, "Wrinkle") || Contains(blob, "Dent"))
        { family = Fam("wrinkle", "Nhăn/Hằn", "Wrinkle/dent"); return true; }
        if (Contains(blob, "Xô") || Contains(blob, "lỏng") || Contains(blob, "Loose") || Contains(blob, "Shifted"))
        { family = Fam("loose", "Xô/Lỏng", "Shifted/loose"); return true; }
        if (Contains(blob, "Tràn keo") || Contains(blob, "Adhesive overflow") || Contains(blob, "Adhesive bleed"))
        { family = Fam("bleed", "Tràn keo", "Adhesive bleed"); return true; }
        if (Contains(blob, "bám dính") || Contains(blob, "Adhesive") || Contains(blob, "Adhesion"))
        { family = Fam("adhesion", "Độ bám dính keo", "Adhesive"); return true; }
        if (Contains(blob, "Biến dạng") || Contains(blob, "Deform"))
        { family = Fam("deform", "Biến dạng", "Deformation"); return true; }
        if (IsColour(blob))
        { family = Fam("colour", "Màu sắc", "Colour"); return true; }
        if (Contains(blob, "Xước") || Contains(blob, "Scratch"))
        { family = Fam("scratch", "Xước", "Scratch"); return true; }
        if (Contains(blob, "Bavia") || Contains(blob, "Burr"))
        { family = Fam("burr", "Bavia", "Burr"); return true; }
        if (Contains(blob, "Lệch") || Contains(blob, "Misalign"))
        { family = Fam("misalign", "Lệch", "Misalignment"); return true; }
        if (Contains(blob, "Loang") || Contains(blob, "Blotch"))
        { family = Fam("blotch", "Loang", "Blotch"); return true; }
        if (Contains(blob, "Dị vật") || Contains(blob, "Foreign"))
        { family = Fam("foreign", "Dị vật", "Foreign matter"); return true; }
        if (Contains(blob, "Bẩn") || Contains(blob, "Dirt"))
        { family = Fam("dirt", "Bẩn", "Dirt"); return true; }
        if (Contains(blob, "Rỗ") || Contains(blob, "Thủng") || Contains(blob, "Pinhol"))
        { family = Fam("pinhole", "Rỗ/Thủng", "Pinhole"); return true; }
        if (Contains(blob, "Lỗi khác") || IsBareOther(blob))
        { family = Fam("other", "Lỗi khác", "Other"); return true; }
        if (Contains(blob, "DIM") && Contains(blob, "OOT"))
        { family = Fam("dim_oot", "DIM-OOT", "DIM-OOT"); return true; }
        return false;
    }

    private static bool IsColour(string blob)
        => Contains(blob, "Màu")
           || (Contains(blob, "Colour") && !Contains(blob, "discolour"))
           || (Contains(blob, "Color") && !Contains(blob, "discolor"));

    private static bool IsBareOther(string blob)
    {
        var t = blob.Trim();
        return t.Equals("Other", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Other defect", StringComparison.OrdinalIgnoreCase);
    }

    private static Family Fam(string key, string vi, string en) => new(key, vi, en);

    private static bool Contains(string hay, string needle)
        => hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? First(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }
}
