using System.Drawing;

namespace BlueType.Agent.Core
{
    public static class ThemeColors
    {
        // 核心背景色 (Deep Space)
        public static readonly Color Background = Color.FromArgb(27, 27, 31);      // #1B1B1F
        public static readonly Color Surface = Color.FromArgb(31, 31, 35);         // #1F1F23
        public static readonly Color SurfaceBright = Color.FromArgb(41, 41, 45);   // #29292D
        
        // 文字颜色
        public static readonly Color OnSurface = Color.FromArgb(228, 225, 230);    // #E4E1E6
        public static readonly Color OnSurfaceVariant = Color.FromArgb(201, 196, 208); // #C9C4D0
        
        // 强调色 (Lavender)
        public static readonly Color Primary = Color.FromArgb(199, 189, 240);      // #C7BDF0
        
        // 状态色
        public static readonly Color Success = Color.FromArgb(165, 214, 167);      // #A5D6A7 (Soft Modern Green)
        
        // 边框与控件背景
        public static readonly Color Stroke = Color.FromArgb(72, 69, 79);          // #48454F
        public static readonly Color ControlBackground = Color.FromArgb(35, 35, 39); // 略深于 Surface
    }
}
