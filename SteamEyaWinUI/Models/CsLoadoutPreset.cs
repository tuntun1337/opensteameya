namespace SteamEyaWinUI.Models;

// 一套配装预设：两阵营各自的「数字槽位 → itemdef」映射。
// 存进 settings.json（AppSettings.LoadoutPresets），供装备页面编辑、一键装配复用。
// 仅记录原版武器（itemdef），不含皮肤——装配时发 0xF000000000000000|itemdef。
internal sealed class CsLoadoutPreset
{
    public string Name { get; set; } = "";

    // key = loadout 数字槽位（1 近战 / 2–7 手枪 / 8–13 中级 / 14–19 步枪 / 34 Zeus），value = itemdef。
    public Dictionary<uint, uint> T { get; set; } = new();

    public Dictionary<uint, uint> Ct { get; set; } = new();

    public CsLoadoutPreset Clone() => new()
    {
        Name = Name,
        T = new Dictionary<uint, uint>(T),
        Ct = new Dictionary<uint, uint>(Ct)
    };

    public Dictionary<uint, uint> SlotsFor(bool counterTerrorist) => counterTerrorist ? Ct : T;

    // 项目内置的默认配装（新用户/无 settings 时的初始值）。槽位 2 起始手枪、3–6 其他手枪、8–12 中级、14–18 步枪。
    public static CsLoadoutPreset Default() => new()
    {
        T = new Dictionary<uint, uint>
        {
            [2] = 4, [3] = 2, [4] = 36, [5] = 1, [6] = 64,
            [8] = 17, [9] = 29, [10] = 23, [11] = 24, [12] = 26,
            [14] = 13, [15] = 7, [16] = 40, [17] = 9, [18] = 11
        },
        Ct = new Dictionary<uint, uint>
        {
            [2] = 61, [3] = 2, [4] = 63, [5] = 1, [6] = 64,
            [8] = 27, [9] = 23, [10] = 26, [11] = 33, [12] = 34,
            [14] = 10, [15] = 60, [16] = 40, [17] = 9, [18] = 38
        }
    };
}

// 一键装配结果：请求装备的槽位数、读回确认数、未确认的槽位说明。
internal sealed record CsLoadoutApplyResult(int Requested, int Confirmed, IReadOnlyList<string> Failures)
{
    public bool IsSuccess => Requested > 0 && Confirmed == Requested && Failures.Count == 0;
}
