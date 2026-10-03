/*
   Copyright 2026 TS SE Tool contributors

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TS_SE_Tool
{
    /// <summary>
    /// Error dialog with a readable summary, the technical details in a selectable box,
    /// and buttons to copy them or open the log folder. Built in code, no designer file.
    /// </summary>
    internal class FormErrorDetails : Form
    {
        internal static void Show(IWin32Window _owner, string _caption, string _summary, string _details)
        {
            using (FormErrorDetails form = new FormErrorDetails(_caption, _summary, _details))
                form.ShowDialog(_owner);
        }

        private FormErrorDetails(string _caption, string _summary, string _details)
        {
            Text = _caption;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(640, 420);
            MinimumSize = new Size(420, 300);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label summary = new Label
            {
                Text = _summary,
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                Margin = new Padding(0, 0, 0, 8)
            };

            TextBox details = new TextBox
            {
                Text = _details,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 8.25f)
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            Button close = new Button { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true };
            Button copy = new Button { Text = "Copy details", AutoSize = true };
            Button openLog = new Button { Text = "Open log folder", AutoSize = true };

            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_caption + Environment.NewLine + _summary + Environment.NewLine + Environment.NewLine + _details);
                    copy.Text = "Copied";
                }
                catch (Exception ex)
                {
                    copy.Text = "Copy failed: " + ex.Message;
                }
            };

            openLog.Click += (s, e) =>
            {
                try
                {
                    Process.Start("explorer.exe", "\"" + Directory.GetCurrentDirectory() + "\"");
                }
                catch { }
            };

            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(openLog);

            layout.Controls.Add(summary, 0, 0);
            layout.Controls.Add(details, 0, 1);
            layout.Controls.Add(buttons, 0, 2);

            Controls.Add(layout);

            AcceptButton = close;
            CancelButton = close;
        }
    }
}
