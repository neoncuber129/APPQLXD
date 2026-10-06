namespace APPQLXD.Core.Export;

/// <summary>
/// Ánh xạ mã placeholder cũ → mã ngắn mới. Khi điền Word, ghi cả hai để mẫu cũ vẫn chạy.
/// </summary>
public static class PlaceholderAliases
{
    /// <summary>Cũ → mới.</summary>
    public static readonly IReadOnlyDictionary<string, string> OldToNew =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Phiếu
            ["$CoQuan"] = "$CQ",
            ["$DonVi"] = "$DV",
            ["$DonViGiao"] = "$DVGiao",
            ["$DonViNhan"] = "$DVNhan",
            ["$TinhChat"] = "$TC",
            ["$HopDong"] = "$HD",
            ["$DonViVanChuyen"] = "$DVVC",
            ["$CoGiaDen"] = "$GiaDen",
            ["$NguoiGiao"] = "$NGiao",
            ["$NguoiNhan"] = "$NNhan",
            ["$DTKiemDinh"] = "$DtKd",
            ["$DTNhanHang"] = "$DtNhan",
            ["$SoBaoBi"] = "$BaoBi",
            ["$NhiemVu"] = "$NV",
            ["$GhiChu"] = "$GC",
            ["$KhoDen"] = "$KDen",
            ["$DoiTuong"] = "$DT",
            ["$SoKhoan"] = "$SKhoan",
            ["$TongSoLuong"] = "$TongSL",
            ["$BangChu"] = "$BC",
            ["$CK_NguoiGiao"] = "$CK_NG",
            ["$CK_NguoiNhan"] = "$CK_NN",
            ["$CK_TaiChinh"] = "$CK_TC",
            ["$CK_NguoiViet"] = "$CK_NV",
            ["$CK_TruongBan"] = "$CK_TB",
            ["$CK_ChiHuy"] = "$CK_CH",
            ["$DonGia"] = "$Gia",
            ["$ThanhTien"] = "$TTien",
            ["$LoaiLo"] = "$LLo",

            // Sổ máy/xe + chung
            ["$NhienLieu"] = "$NL",
            ["$MauSo"] = "$MS",
            ["$DienGiai"] = "$DGiai",
            ["$DiGiai"] = "$DGiai", // biến thể thường gặp trên mẫu Word
            ["$DinhMuc"] = "$DM",
            ["$ThucTe"] = "$TT",
            ["$GioMay"] = "$GMay",
            ["$XangNhap"] = "$XNhap",
            ["$XangXuat"] = "$XXuat",
            ["$XangTon"] = "$XTon",
            ["$DauNhap"] = "$DNhap",
            ["$DauXuat"] = "$DXuat",
            ["$DauTon"] = "$DTon",
            ["$TongXang"] = "$TXang",
            ["$TongDau"] = "$TDau",
            ["$C_DienGiai"] = "$C_DGiai",
            ["$C_DiGiai"] = "$C_DGiai",
            ["$O_DienGiai"] = "$O_DGiai",
            ["$O_DiGiai"] = "$O_DGiai",
            // Tồn cuối / mang sang — $T_* (alias cũ $C_XTon/$C_DTon)
            ["$T_DienGiai"] = "$T_DGiai",
            ["$T_DiGiai"] = "$T_DGiai",
            ["$C_XTon"] = "$T_XTon",
            ["$C_DTon"] = "$T_DTon",
            ["$C_SoKm"] = "$C_SoKm",
            ["$C_GioMay"] = "$C_GMay",
            ["$C_DinhMuc"] = "$C_DM",
            ["$C_ThucTe"] = "$C_TT",
            ["$C_Vuot"] = "$C_Vuot",
            ["$C_Thieu"] = "$C_Thieu",
            ["$C_XangNhap"] = "$C_XNhap",
            ["$C_XangXuat"] = "$C_XXuat",
            ["$C_DauNhap"] = "$C_DNhap",
            ["$C_DauXuat"] = "$C_DXuat",

            // Sổ tàu
            ["$LoaiTau"] = "$LTau",
            ["$MayChinh"] = "$MChinh",
            ["$MayPhu"] = "$MPhu",
            ["$GioNeo"] = "$GNeo",
            ["$GNao"] = "$GNeo", // lỗi đánh máy phổ biến trên mẫu (Giờ neo / tại bến)
            ["$C_GNao"] = "$C_GNeo",
            ["$GioCx25"] = "$Cx25",
            ["$GioCx50"] = "$Cx50",
            ["$GioCx75"] = "$Cx75",
            ["$GioCx100"] = "$Cx100",
            ["$TongGioChinh"] = "$TongGio",
            ["$GioPhu"] = "$GPhu",
            ["$XangDung"] = "$XDung",
            ["$DauDung"] = "$DDung",
            ["$TxXang"] = "$XDung", // T.thụ xăng (Cộng giờ HĐ)
            ["$TxDiesel"] = "$DDung", // T.thụ Diesel
            ["$Diesel"] = "$DDung",
            ["$TongDong"] = "$TDong",
            ["$C_MayChinh"] = "$C_MChinh",
            ["$C_GioNeo"] = "$C_GNeo",
            ["$C_GioCx25"] = "$C_Cx25",
            ["$C_GioCx50"] = "$C_Cx50",
            ["$C_GioCx75"] = "$C_Cx75",
            ["$C_GioCx100"] = "$C_Cx100",
            ["$C_TongGioChinh"] = "$C_TongGio",
            ["$C_MayPhu"] = "$C_MPhu",
            ["$C_GioPhu"] = "$C_GPhu",
            ["$C_XangDung"] = "$C_XDung",
            ["$C_DauDung"] = "$C_DDung",
            ["$C_TxXang"] = "$C_XDung",
            ["$C_TxDiesel"] = "$C_DDung",
            ["$C_Diesel"] = "$C_DDung",

            // NXT
            ["$TonCuoi"] = "$TCuoi",
        };

    public static Dictionary<string, string> Expand(IReadOnlyDictionary<string, string> values)
    {
        var map = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var (oldToken, newToken) in OldToNew)
        {
            if (map.TryGetValue(newToken, out var neu) && !map.ContainsKey(oldToken))
                map[oldToken] = neu;
            if (map.TryGetValue(oldToken, out var cu) && !map.ContainsKey(newToken))
                map[newToken] = cu;
        }

        return map;
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ExpandRows(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
        rows.Select(Expand).Cast<IReadOnlyDictionary<string, string>>().ToList();
}
