namespace ChiliMusic;

public sealed record ColorTheme(string Id, string Name, string Background, string Sidebar, string Surface, string Input, string Accent, string AccentHover, string Selected, string Border, string Text = "#263443", string Muted = "#667085", bool Dark = false);
public sealed record ThemeChoice(string Id, string Name, string Preview) { public override string ToString() => Name; }

public static class ThemeCatalog
{
    public static IReadOnlyList<ColorTheme> All { get; } = [
        new("moon_white", "月华清霜", "#EDF3F7", "#EDF3F7", "#F9FBFC", "#FCFDFE", "#3478F6", "#2968DB", "#DDE9FB", "#D5E0E8"),
        new("peach_blossom", "花笺春信", "#FFF5F8", "#FFF5F8", "#FFF9FB", "#FFFCFD", "#D97898", "#C55F82", "#F8DFE8", "#EFD6DF", "#48303A", "#876776"),
        new("court_green", "庭芳新绿", "#F3F6ED", "#F3F6ED", "#FAFBF7", "#FCFDF9", "#6D9270", "#587D5D", "#DDE9D8", "#D8E1D0", "#314238", "#6F7F73"),
        new("daybreak_blue", "东方既白", "#F1F5F9", "#F1F5F9", "#FAFCFE", "#FCFDFE", "#5277A3", "#41658E", "#DBE6F2", "#D4DEE8", "#293A4C", "#657689"),
        new("spring_olive", "春风入野", "#F8F6ED", "#F8F6ED", "#FCFBF5", "#FEFDF8", "#7E9A62", "#687F51", "#E5EAD7", "#E2DECC", "#3C4335", "#777D6C"),
        new("soft_indigo", "清澜映月", "#F6F4FB", "#F6F4FB", "#FBFAFD", "#FDFCFE", "#7567B3", "#63559E", "#E6E0F5", "#DED8EA", "#39354B", "#777086"),
        new("forest_pulse", "青森流响", "#EEF5EF", "#EEF5EF", "#F7FBF8", "#FAFCFA", "#397A70", "#2E685F", "#D7EAE5", "#CFDFD7", "#29423D", "#637C75"),
        new("coral_alert", "流霞映晚", "#FFF5F0", "#FFF5F0", "#FFF9F6", "#FFFCFA", "#E46E63", "#CF594F", "#F9DED8", "#ECD5CE", "#513632", "#896E68"),
        new("neon_spectrum", "霓虹频谱", "#F9F4FC", "#F9F4FC", "#FCF9FE", "#FEFCFF", "#A25CD1", "#8945B8", "#EEE0F7", "#E4D6ED", "#40324C", "#796A84"),
        new("ink_blue", "墨夜星河", "#0E1728", "#0E1728", "#18263C", "#101C2E", "#6F9FEF", "#8BB4F6", "#203B63", "#2B415E", "#F2F5FA", "#AEBBD0", true),
        new("deep_sea_mint", "海雾青岚", "#0D201F", "#0D201F", "#173532", "#102926", "#71C7AE", "#8AD8C2", "#214B44", "#2C4F49", "#EDF9F5", "#AACBC2", true),
        new("midnight_violet", "紫夜星河", "#17152B", "#17152B", "#282440", "#1C1931", "#9885E8", "#AD9AF1", "#37305C", "#413A61", "#F4F1FC", "#BBB4D4", true),
        new("caramel_mocha", "醇棕晨雾", "#251914", "#251914", "#3A2921", "#2D201A", "#C58B58", "#D6A06E", "#51382B", "#5C4133", "#FBF3EC", "#D2BCAE", true)
    ];
    public static IReadOnlyList<ThemeChoice> Choices { get; } = new[] { new ThemeChoice("System", "跟随系统", "#8494A8") }.Concat(All.Select(t => new ThemeChoice(t.Id, t.Name, t.Accent))).ToArray();
    public static string Normalize(string mode) => mode switch { "Light" => "moon_white", "Dark" => "ink_blue", _ => Choices.Any(t => t.Id == mode) ? mode : "System" };
    public static ColorTheme Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
