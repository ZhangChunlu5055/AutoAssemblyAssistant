using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoAssemblyAddin.UI;

[ComVisible(false)]
internal sealed class AssistantPane : UserControl
{
    private static readonly Color Accent = Color.FromArgb(35, 94, 180);
    private readonly TextBox _context;
    private readonly RichTextBox _history;
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly FlowLayoutPanel _shortcuts;
    private readonly GroupBox _plan;
    private readonly TextBox _planSummary;
    private readonly ComboBox _configuration;
    private readonly Button _execute;
    private readonly Label _status;
    private bool _motionMode;
    private readonly Font _bodyFont = new Font("Microsoft YaHei UI", 9F);
    private readonly Font _headingFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
    private readonly Font _roleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

    public event Action<string>? CommandSubmitted;
    public event Action<string>? ReplacementAccepted;
    public event Action? ReplacementCancelled;
    public event Action? MotionAccepted;

    public AssistantPane()
    {
        Font = _bodyFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(246, 248, 251);
        ForeColor = Color.FromArgb(30, 41, 59);
        Size = new Size(360, 760);
        AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8,
            Padding = new Padding(12), MinimumSize = new Size(240, 540)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        heading.Controls.Add(new Label
        {
            Text = "选中模组，直接在装配体中操作", Dock = DockStyle.Bottom,
            Height = 23, ForeColor = Color.FromArgb(100, 116, 139)
        });
        heading.Controls.Add(new Label
        {
            Text = "AutoAssembly · 装配助手", Font = _headingFont,
            Dock = DockStyle.Top, Height = 29, ForeColor = Accent
        });
        layout.Controls.Add(heading, 0, 0);

        _context = ReadOnlyBox();
        _context.BackColor = Color.FromArgb(234, 240, 248);
        _context.AccessibleName = "当前 SolidWorks 选择";
        layout.Controls.Add(_context, 0, 1);

        _shortcuts = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Margin = new Padding(0, 4, 0, 5)
        };
        foreach (var text in new[] { "查看选中模组", "替换该模组", "导出位姿", "帮助" })
        {
            var button = MakeButton(text);
            button.AutoSize = true;
            button.Click += (_, _) => CommandSubmitted?.Invoke(text);
            _shortcuts.Controls.Add(button);
        }
        layout.Controls.Add(_shortcuts, 0, 2);

        _history = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White,
            BorderStyle = BorderStyle.None, DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical, Margin = new Padding(0, 0, 0, 8),
            AccessibleName = "装配助手对话记录"
        };
        layout.Controls.Add(_history, 0, 3);

        _plan = new GroupBox
        {
            Text = "待执行的替换", Dock = DockStyle.Fill,
            Height = 188, Visible = false, Margin = new Padding(0, 0, 0, 8)
        };
        var planLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6)
        };
        planLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        planLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _planSummary = ReadOnlyBox();
        _planSummary.AccessibleName = "替换计划摘要";
        _configuration = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "候选模组配置", DrawMode = DrawMode.OwnerDrawFixed
        };
        _configuration.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                using var brush = new SolidBrush(e.ForeColor);
                e.Graphics.DrawString("配置：" + _configuration.Items[e.Index], e.Font ?? Font, brush, e.Bounds);
            }
            e.DrawFocusRectangle();
        };
        var planActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _execute = MakeButton("执行替换", true);
        _execute.Click += (_, _) =>
        {
            if (_motionMode) MotionAccepted?.Invoke();
            else ReplacementAccepted?.Invoke(_configuration.SelectedItem as string ?? string.Empty);
        };
        var cancel = MakeButton("取消");
        cancel.Click += (_, _) => ReplacementCancelled?.Invoke();
        planActions.Controls.Add(_execute);
        planActions.Controls.Add(cancel);
        planLayout.Controls.Add(_planSummary, 0, 0);
        planLayout.Controls.Add(_configuration, 0, 1);
        planLayout.Controls.Add(planActions, 0, 2);
        _plan.Controls.Add(planLayout);
        layout.Controls.Add(_plan, 0, 4);

        _input = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle, AcceptsReturn = true,
            AccessibleName = "输入指令", MaxLength = 2000
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
        };
        layout.Controls.Add(_input, 0, 5);
        var sendRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        sendRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        sendRow.Controls.Add(new Label
        {
            Text = "Enter 发送 · Shift+Enter 换行", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(100, 116, 139)
        }, 0, 0);
        _send = MakeButton("发送", true);
        _send.Dock = DockStyle.Fill;
        _send.Click += (_, _) => Submit();
        sendRow.Controls.Add(_send, 1, 0);
        layout.Controls.Add(sendRow, 0, 6);
        _status = new Label
        {
            Text = "本地指令模式 · 已连接 SolidWorks", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 116, 139), TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(_status, 0, 7);
    }

    public void SetContext(string text)
    {
        if (_context.Text != text) _context.Text = text;
    }

    public void AddMessage(string role, string text)
    {
        // Bound the UI history in long-running SW sessions.
        if (_history.TextLength > 100000) _history.Clear();
        _history.SelectionStart = _history.TextLength;
        _history.SelectionColor = role == "你" ? Accent : Color.FromArgb(30, 41, 59);
        _history.SelectionFont = _roleFont;
        _history.AppendText($"{role}  {DateTime.Now:HH:mm}\r\n");
        _history.SelectionFont = _bodyFont;
        _history.SelectionColor = Color.FromArgb(51, 65, 85);
        _history.AppendText(text + "\r\n\r\n");
        _history.SelectionStart = _history.TextLength;
        _history.ScrollToCaret();
    }

    public void ShowPlan(string summary, string[] configurations, string preferredConfiguration)
    {
        _motionMode = false;
        _plan.Text = "待执行的替换";
        _execute.Text = "执行替换";
        _configuration.Visible = true;
        _planSummary.Text = summary;
        _configuration.Items.Clear();
        _configuration.Items.AddRange(configurations);
        var index = Array.IndexOf(configurations, preferredConfiguration);
        _configuration.SelectedIndex = index >= 0 ? index : 0;
        _plan.Visible = true;
    }

    public void ShowMotionPlan(string summary)
    {
        _motionMode = true;
        _plan.Text = "待执行的位姿调整";
        _execute.Text = "执行调整";
        _configuration.Visible = false;
        _planSummary.Text = summary;
        _plan.Visible = true;
    }

    public void HidePlan() => _plan.Visible = false;

    public void SetBusy(bool busy)
    {
        _send.Enabled = _input.Enabled = _shortcuts.Enabled = _plan.Enabled = !busy;
        UseWaitCursor = busy;
        _status.Text = busy ? "正在与 SolidWorks 交互…" : "本地指令模式 · 已连接 SolidWorks";
    }

    private void Submit()
    {
        if (!_send.Enabled || string.IsNullOrWhiteSpace(_input.Text)) return;
        var text = _input.Text.Trim();
        _input.Clear();
        CommandSubmitted?.Invoke(text);
    }

    private static TextBox ReadOnlyBox() => new TextBox
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(246, 248, 251),
        ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 4, 0, 4)
    };

    private static Button MakeButton(string text, bool primary = false) => new Button
    {
        Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Accent : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(51, 65, 85),
        Padding = new Padding(4, 1, 4, 1), Margin = new Padding(0, 2, 6, 2),
        Cursor = Cursors.Hand
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _bodyFont.Dispose();
            _headingFont.Dispose();
            _roleFont.Dispose();
        }
    }
}
