using SteamEyaWinUI.Localization;

namespace SteamEyaWinUI.Models;

internal enum CsWeaponCategory
{
    Pistol,  // 手枪，loadout slot 2–7 (secondary0–5)
    Mid,     // 中级：微冲/霰弹/机枪，loadout slot 8–13 (smg0–5)
    Rifle,   // 步枪/狙，loadout slot 14–19 (rifle0–5)
    Taser    // Zeus 电击枪，loadout slot 34 (equipment2)
    // 近战刀按需求忽略（对快速上号无影响）。
}

// CS2 购买菜单分组（装备页面按此分列）。起始手枪 = secondary0(slot2)，其他手枪 = secondary1–5(slot3–7)。
internal enum CsLoadoutGroup
{
    StarterPistol,  // 起始手枪：slot 2。T 只有 Glock；CT 可选 P2000 / USP-S。
    OtherPistol,    // 其他手枪：slot 3–7。
    Mid,            // 中级：slot 8–13。
    Rifle,          // 步枪/狙：slot 14–19。
    Zeus            // 电击枪：slot 34。
}

// 一把可装备武器的静态定义。Def=CS2 item definition index；T/Ct=该枪可被哪一阵营使用。
// IconFile 对应 Assets/weapons/<IconFile>.svg（取自 Juknum/counter-strike-icons，CS2 游戏内图标）。
// 数据由账号 SO 缓存实测 + Valve items_game.txt 双向确认（见 steameya-weapon-loadout-probe 记忆）。
internal sealed record CsWeapon(
    uint Def,
    string ClassName,
    string DisplayName,
    CsWeaponCategory Category,
    bool T,
    bool Ct,
    string IconFile,
    bool Starter = false)  // Starter=起始手枪（flexible_loadout_slot=secondary0）：Glock / P2000 / USP-S。
{
    public string IconUri => $"ms-appx:///Assets/weapons/{IconFile}.svg";

    // 显示名走 i18n（键 Weapon_<def>）；DisplayName 仅作目录内可读注释/回退。
    public string LocalizedName => Loc.T($"Weapon_{Def}");

    public bool UsableBy(bool counterTerrorist) => counterTerrorist ? Ct : T;
}

internal static class CsWeaponCatalog
{
    // loadout 数字槽位区间（实测确认：每类 5 个位置，命名槽 0–4；slot 7/13/19 不存在）。
    // 手枪 secondary0–4=2–6，中级 smg0–4=8–12，步枪 rifle0–4=14–18。
    public const uint StarterPistolSlot = 2;   // secondary0（起始手枪）
    public const uint TaserSlot = 34;
    public static readonly uint[] OtherPistolSlots = [3, 4, 5, 6];      // secondary1–4
    public static readonly uint[] PistolSlots = [2, 3, 4, 5, 6];
    public static readonly uint[] MidSlots = [8, 9, 10, 11, 12];
    public static readonly uint[] RifleSlots = [14, 15, 16, 17, 18];

    public static readonly IReadOnlyList<CsWeapon> All =
    [
        // 近战刀按需求忽略（对快速上号无影响），不在目录里。

        // ---- 手枪 secondary / slot 2–7 ----
        new(1, "weapon_deagle", "沙漠之鹰", CsWeaponCategory.Pistol, T: true, Ct: true, "deagle"),
        new(2, "weapon_elite", "双持贝瑞塔", CsWeaponCategory.Pistol, T: true, Ct: true, "elite"),
        new(3, "weapon_fiveseven", "Five-SeveN", CsWeaponCategory.Pistol, T: false, Ct: true, "fiveseven"),
        new(4, "weapon_glock", "Glock-18", CsWeaponCategory.Pistol, T: true, Ct: false, "glock", Starter: true),
        new(30, "weapon_tec9", "Tec-9", CsWeaponCategory.Pistol, T: true, Ct: false, "tec9"),
        new(32, "weapon_hkp2000", "P2000", CsWeaponCategory.Pistol, T: false, Ct: true, "hkp2000", Starter: true),
        new(36, "weapon_p250", "P250", CsWeaponCategory.Pistol, T: true, Ct: true, "p250"),
        new(61, "weapon_usp_silencer", "USP-S", CsWeaponCategory.Pistol, T: false, Ct: true, "usp_silencer", Starter: true),
        new(63, "weapon_cz75a", "CZ75-Auto", CsWeaponCategory.Pistol, T: true, Ct: true, "cz75a"),
        new(64, "weapon_revolver", "R8 转轮", CsWeaponCategory.Pistol, T: true, Ct: true, "revolver"),

        // ---- 中级：微冲 / 霰弹 / 机枪，slot 8–13 ----
        new(17, "weapon_mac10", "MAC-10", CsWeaponCategory.Mid, T: true, Ct: false, "mac10"),
        new(19, "weapon_p90", "P90", CsWeaponCategory.Mid, T: true, Ct: true, "p90"),
        new(23, "weapon_mp5sd", "MP5-SD", CsWeaponCategory.Mid, T: true, Ct: true, "mp5sd"),
        new(24, "weapon_ump45", "UMP-45", CsWeaponCategory.Mid, T: true, Ct: true, "ump45"),
        new(25, "weapon_xm1014", "XM1014", CsWeaponCategory.Mid, T: true, Ct: true, "xm1014"),
        new(26, "weapon_bizon", "PP-野牛", CsWeaponCategory.Mid, T: true, Ct: true, "bizon"),
        new(27, "weapon_mag7", "MAG-7", CsWeaponCategory.Mid, T: false, Ct: true, "mag7"),
        new(28, "weapon_negev", "Negev", CsWeaponCategory.Mid, T: true, Ct: true, "negev"),
        new(29, "weapon_sawedoff", "截短霰弹枪", CsWeaponCategory.Mid, T: true, Ct: false, "sawedoff"),
        new(33, "weapon_mp7", "MP7", CsWeaponCategory.Mid, T: true, Ct: true, "mp7"),
        new(34, "weapon_mp9", "MP9", CsWeaponCategory.Mid, T: false, Ct: true, "mp9"),
        new(35, "weapon_nova", "Nova", CsWeaponCategory.Mid, T: true, Ct: true, "nova"),
        new(14, "weapon_m249", "M249", CsWeaponCategory.Mid, T: true, Ct: true, "m249"),

        // ---- 步枪 / 狙，slot 14–19 ----
        new(7, "weapon_ak47", "AK-47", CsWeaponCategory.Rifle, T: true, Ct: false, "ak47"),
        new(8, "weapon_aug", "AUG", CsWeaponCategory.Rifle, T: false, Ct: true, "aug"),
        new(9, "weapon_awp", "AWP", CsWeaponCategory.Rifle, T: true, Ct: true, "awp"),
        new(10, "weapon_famas", "FAMAS", CsWeaponCategory.Rifle, T: false, Ct: true, "famas"),
        new(11, "weapon_g3sg1", "G3SG1", CsWeaponCategory.Rifle, T: true, Ct: false, "g3sg1"),
        new(13, "weapon_galilar", "加利尔 AR", CsWeaponCategory.Rifle, T: true, Ct: false, "galilar"),
        new(16, "weapon_m4a1", "M4A4", CsWeaponCategory.Rifle, T: false, Ct: true, "m4a1"),
        new(38, "weapon_scar20", "SCAR-20", CsWeaponCategory.Rifle, T: false, Ct: true, "scar20"),
        new(39, "weapon_sg556", "SG 553", CsWeaponCategory.Rifle, T: true, Ct: false, "sg556"),
        new(40, "weapon_ssg08", "SSG 08", CsWeaponCategory.Rifle, T: true, Ct: true, "ssg08"),
        new(60, "weapon_m4a1_silencer", "M4A1-S", CsWeaponCategory.Rifle, T: false, Ct: true, "m4a1_silencer"),

        // Zeus 电击枪按需求忽略（所有人都会装），不在目录里。
    ];

    private static readonly Dictionary<uint, CsWeapon> ByDefMap = All.ToDictionary(w => w.Def);

    public static CsWeapon? ByDef(uint def) => ByDefMap.GetValueOrDefault(def);

    public static IReadOnlyList<uint> SlotsFor(CsWeaponCategory category) => category switch
    {
        CsWeaponCategory.Pistol => PistolSlots,
        CsWeaponCategory.Mid => MidSlots,
        CsWeaponCategory.Rifle => RifleSlots,
        CsWeaponCategory.Taser => [TaserSlot],
        _ => []
    };

    public static CsWeaponCategory CategoryForSlot(uint slot) => slot switch
    {
        >= 8 and <= 12 => CsWeaponCategory.Mid,
        >= 14 and <= 18 => CsWeaponCategory.Rifle,
        TaserSlot => CsWeaponCategory.Taser,
        _ => CsWeaponCategory.Pistol
    };

    // 装备页面按此顺序展示类别。
    public static readonly CsWeaponCategory[] EditorCategories =
        [CsWeaponCategory.Pistol, CsWeaponCategory.Mid, CsWeaponCategory.Rifle, CsWeaponCategory.Taser];

    // 某阵营在某类别下可选的武器（用于装备页面的候选列表）。
    public static IEnumerable<CsWeapon> ForTeamCategory(bool counterTerrorist, CsWeaponCategory category) =>
        All.Where(w => w.Category == category && w.UsableBy(counterTerrorist));

    // ---- 购买菜单分组（拖拽式装备页面用）----

    public static readonly CsLoadoutGroup[] EditorGroups =
        [CsLoadoutGroup.StarterPistol, CsLoadoutGroup.OtherPistol, CsLoadoutGroup.Mid, CsLoadoutGroup.Rifle];

    public static CsLoadoutGroup GroupOf(CsWeapon weapon) => weapon.Category switch
    {
        CsWeaponCategory.Pistol => weapon.Starter ? CsLoadoutGroup.StarterPistol : CsLoadoutGroup.OtherPistol,
        CsWeaponCategory.Mid => CsLoadoutGroup.Mid,
        CsWeaponCategory.Rifle => CsLoadoutGroup.Rifle,
        CsWeaponCategory.Taser => CsLoadoutGroup.Zeus,
        _ => CsLoadoutGroup.OtherPistol
    };

    public static IReadOnlyList<uint> SlotsForGroup(CsLoadoutGroup group) => group switch
    {
        CsLoadoutGroup.StarterPistol => [StarterPistolSlot],
        CsLoadoutGroup.OtherPistol => OtherPistolSlots,
        CsLoadoutGroup.Mid => MidSlots,
        CsLoadoutGroup.Rifle => RifleSlots,
        CsLoadoutGroup.Zeus => [TaserSlot],
        _ => []
    };

    public static CsLoadoutGroup GroupForSlot(uint slot) => slot switch
    {
        StarterPistolSlot => CsLoadoutGroup.StarterPistol,
        >= 3 and <= 6 => CsLoadoutGroup.OtherPistol,
        >= 8 and <= 12 => CsLoadoutGroup.Mid,
        >= 14 and <= 18 => CsLoadoutGroup.Rifle,
        TaserSlot => CsLoadoutGroup.Zeus,
        _ => CsLoadoutGroup.OtherPistol
    };

    public static string GroupName(CsLoadoutGroup group) => group switch
    {
        CsLoadoutGroup.StarterPistol => "起始手枪",
        CsLoadoutGroup.OtherPistol => "其他手枪",
        CsLoadoutGroup.Mid => "中级",
        CsLoadoutGroup.Rifle => "步枪 / 狙",
        CsLoadoutGroup.Zeus => "Zeus 电击枪",
        _ => ""
    };

    // 某阵营在某购买菜单分组下可选的武器（装备页面候选列表）。
    public static IEnumerable<CsWeapon> ForTeamGroup(bool counterTerrorist, CsLoadoutGroup group) =>
        All.Where(w => GroupOf(w) == group && w.UsableBy(counterTerrorist));
}
