using MudBlazor;

namespace MessagePusher.Web.Theme;

/// <summary>
/// 全局主题：统一品牌色（青碧 Teal）、中性灰阶与组件圆角，
/// 浅色 / 深色两套调色板保持同一品牌基因。
/// </summary>
public static class AppTheme
{
    // ---- 品牌色板（Teal 系）----
    private const string Brand500 = "#0F766E";   // 主色
    private const string Brand600 = "#115E59";   // 深主色 / Secondary
    private const string Brand700 = "#134E4A";   // 最深

    public static MudTheme Current { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Brand500,
            PrimaryContrastText = "#FFFFFF",
            Secondary = Brand600,
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#0891B2",
            TertiaryContrastText = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#111827",
            Background = "#F6F8F9",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#374151",
            DrawerIcon = "#6B7280",
            TextPrimary = "#111827",
            TextSecondary = "#6B7280",
            TextDisabled = "#9CA3AF",
            ActionDefault = "#6B7280",
            LinesDefault = "#E5E7EB",
            LinesInputs = "#D1D5DB",
            Divider = "#E5E7EB",
            TableStriped = "#FAFBFB",
            TableHover = "rgba(15, 118, 110, 0.05)",
            Dark = "#111827"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#2DD4BF",
            PrimaryContrastText = "#042F2E",
            Secondary = "#5EEAD4",
            SecondaryContrastText = "#042F2E",
            Tertiary = "#22D3EE",
            AppbarBackground = "#0B1220",
            AppbarText = "#F3F4F6",
            Background = "#0B1220",
            Surface = "#111C2F",
            DrawerBackground = "#0E1626",
            DrawerText = "#D1D5DB",
            DrawerIcon = "#9CA3AF",
            TextPrimary = "#F3F4F6",
            TextSecondary = "#9CA3AF",
            TextDisabled = "#6B7280",
            ActionDefault = "#9CA3AF",
            LinesDefault = "#1F2A3D",
            LinesInputs = "#2A3649",
            Divider = "#1F2A3D",
            TableHover = "rgba(45, 212, 191, 0.06)"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            AppbarHeight = "60px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Roboto", "\"Helvetica Neue\"", "\"Noto Sans SC\"", "\"PingFang SC\"", "\"Microsoft YaHei\"", "Arial", "sans-serif" },
                FontSize = ".875rem",
                FontWeight = "400",
                LineHeight = "1.6"
            },
            H4 = new H4Typography
            {
                FontSize = "1.5rem",
                FontWeight = "600",
                LineHeight = "1.35"
            },
            H5 = new H5Typography
            {
                FontSize = "1.25rem",
                FontWeight = "600"
            },
            H6 = new H6Typography
            {
                FontSize = "1.05rem",
                FontWeight = "600"
            },
            Subtitle1 = new Subtitle1Typography
            {
                FontSize = "1rem",
                FontWeight = "500"
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontSize = ".875rem",
                FontWeight = "500"
            },
            Button = new ButtonTypography
            {
                TextTransform = "none",
                FontWeight = "500"
            }
        }
    };
}
