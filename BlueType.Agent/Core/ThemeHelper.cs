using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlueType.Agent.Core
{
    public static class ThemeHelper
    {
        public static void ApplyDarkTheme(Form form)
        {
            form.BackColor = ThemeColors.Background;
            form.ForeColor = ThemeColors.OnSurface;

            foreach (Control control in form.Controls)
            {
                ApplyToControl(control);
            }
        }

        public static void ApplyToControl(Control control)
        {
            // 递归处理容器内部控件
            if (control.HasChildren)
            {
                foreach (Control child in control.Controls)
                {
                    ApplyToControl(child);
                }
            }

            // 根据控件类型应用样式
            switch (control)
            {
                case Label label:
                    label.ForeColor = ThemeColors.OnSurface;
                    break;

                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = ThemeColors.SurfaceBright;
                    button.ForeColor = ThemeColors.Primary;
                    button.FlatAppearance.BorderColor = ThemeColors.Stroke;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 70);
                    break;

                case TextBox textBox:
                    textBox.BackColor = ThemeColors.ControlBackground;
                    textBox.ForeColor = ThemeColors.OnSurface;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case NumericUpDown numericUpDown:
                    numericUpDown.BackColor = ThemeColors.ControlBackground;
                    numericUpDown.ForeColor = ThemeColors.OnSurface;
                    numericUpDown.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case GroupBox groupBox:
                    groupBox.ForeColor = ThemeColors.Primary; // 标题颜色
                    groupBox.BackColor = ThemeColors.Background;
                    break;

                case Panel panel:
                    panel.BackColor = ThemeColors.Background;
                    break;

                case ListBox listBox:
                    listBox.BackColor = ThemeColors.ControlBackground;
                    listBox.ForeColor = ThemeColors.OnSurface;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                default:
                    control.BackColor = ThemeColors.Background;
                    control.ForeColor = ThemeColors.OnSurface;
                    break;
            }
        }

        public static void ApplyToContextMenu(ContextMenuStrip menu)
        {
            menu.BackColor = ThemeColors.Surface;
            menu.ForeColor = ThemeColors.OnSurface;
            menu.ShowImageMargin = false; // 极简风格

            foreach (ToolStripItem item in menu.Items)
            {
                ApplyToMenuItem(item);
            }
        }

        private static void ApplyToMenuItem(ToolStripItem item)
        {
            item.BackColor = ThemeColors.Surface;
            item.ForeColor = ThemeColors.OnSurface;

            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                foreach (ToolStripItem dropDownItem in menuItem.DropDownItems)
                {
                    ApplyToMenuItem(dropDownItem);
                }
            }
        }
    }
}
