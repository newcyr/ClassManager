using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClassManager.Core;
using ClassManager.Seewo;
using Microsoft.Win32;

namespace ClassManager.App;

public partial class MainWindow : Window
{
    private AppConfig _config;
    private readonly List<ComboBox> _weekCombos = new();
    private RingPlayer? _preview;

    public ObservableCollection<string> SubjectOptions { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _config = App.Current.Config.Clone();

        // 适配高 DPI / 小屏：窗口不超过工作区
        Width = Math.Min(1100, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 24);
        MinWidth = Math.Min(960, Width);
        MinHeight = Math.Min(600, Height);

        Loaded += (_, _) => BindAll();
    }

    private void BindAll()
    {
        ConfigStore.Normalize(_config);

        SubjectOptions.Clear();
        foreach (var s in _config.Subjects) SubjectOptions.Add(s);

        ListSubjects.ItemsSource = null;
        ListSubjects.ItemsSource = _config.Subjects;

        var selectedId = (ListTimetables.SelectedItem as Timetable)?.Id;
        ListTimetables.ItemsSource = null;
        ListTimetables.ItemsSource = _config.Timetables;
        ListTimetables.SelectedItem = _config.Timetables.FirstOrDefault(t => t.Id == selectedId) ?? _config.Timetables.FirstOrDefault();

        BuildWeekMap();

        GridAlarms.ItemsSource = null;
        GridAlarms.ItemsSource = _config.Alarms;

        GridSmartUrls.ItemsSource = null;
        GridSmartUrls.ItemsSource = _config.SmartUrls;

        AccountList.ItemsSource = null;
        AccountList.ItemsSource = _config.SeewoAccounts;

        TxtDefaultAdvance.Text = _config.DefaultAdvanceMinutes.ToString();
        TxtRingtone.Text = _config.RingtonePath;
        ChkAlarmEnabled.IsChecked = _config.AlarmEnabled;
        ChkSeewoLogin.IsChecked = _config.SeewoLoginEnabled;
        ChkAutoStart.IsChecked = _config.StartWithWindows;

        RebindingEntries();
        RefreshStatus();
    }

    private Timetable? SelectedTimetable => ListTimetables.SelectedItem as Timetable;

    private void RebindingEntries()
    {
        var tt = SelectedTimetable;
        GridEntries.ItemsSource = null;
        GridEntries.ItemsSource = tt?.Entries;
        TxtCurrentTimetable.Text = tt == null ? "当前课程表" : $"当前课程表：{tt.Name}（{tt.Entries.Count} 节课）";
        TxtTimetableName.Text = tt?.Name ?? "";
    }

    private void BuildWeekMap()
    {
        WeekMapPanel.Children.Clear();
        _weekCombos.Clear();

        for (int i = 0; i < 7; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
            var label = new TextBlock
            {
                Text = ConfigStore.WeekdayNames[i],
                Width = 46,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            var combo = new ComboBox
            {
                ItemsSource = _config.Timetables,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id",
                MinHeight = 26,
                Margin = new Thickness(4, 0, 0, 0)
            };
            var idx = i;
            combo.SelectedValue = i < _config.WeekMapping.Count ? _config.WeekMapping[i] : "";
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedValue is string id) _config.WeekMapping[idx] = id;
            };
            row.Children.Add(combo);
            WeekMapPanel.Children.Add(row);
            _weekCombos.Add(combo);
        }
    }

    public void RefreshStatus()
    {
        try
        {
            var now = DateTime.Now;
            var tt = ScheduleEngine.GetTimetableForDate(_config, now);
            var todayCount = ScheduleEngine.GetTriggersForDate(_config, now).Count(t => t.IsCourse);
            StatusText.Text = $"{now:MM-dd} {ConfigStore.WeekdayNames[ScheduleEngine.DayIndex(now)]} 使用：{tt?.Name ?? "未映射"}（{todayCount} 节）　｜　数据目录：{AppPaths.DataDir}";

            var next = ScheduleEngine.GetNextTrigger(_config, now);
            NextText.Text = next == null
                ? "暂无待触发提醒"
                : $"下一次提醒：{next.FireAt:MM-dd HH:mm} {next.Subject}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "状态刷新失败：" + ex.Message;
        }
    }

    // ---------------- 课程表 ----------------

    private void ListTimetables_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebindingEntries();

    private void BtnNewTimetable_Click(object sender, RoutedEventArgs e)
    {
        var tt = new Timetable
        {
            Id = "tt-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "新课程表"
        };
        _config.Timetables.Add(tt);
        ListTimetables.ItemsSource = null;
        ListTimetables.ItemsSource = _config.Timetables;
        ListTimetables.SelectedItem = tt;
        BuildWeekMap();
        RefreshStatus();
    }

    private void BtnDeleteTimetable_Click(object sender, RoutedEventArgs e)
    {
        var tt = SelectedTimetable;
        if (tt == null) return;
        if (_config.Timetables.Count <= 1)
        {
            MessageBox.Show("至少保留一张课程表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"确定删除课程表「{tt.Name}」？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _config.Timetables.Remove(tt);
        for (int i = 0; i < _config.WeekMapping.Count; i++)
        {
            if (_config.WeekMapping[i] == tt.Id)
                _config.WeekMapping[i] = _config.Timetables[0].Id;
        }
        BindAll();
    }

    private void BtnRenameTimetable_Click(object sender, RoutedEventArgs e)
    {
        var tt = SelectedTimetable;
        if (tt == null) return;
        var name = TxtTimetableName.Text.Trim();
        if (name.Length == 0) return;
        tt.Name = name;
        ListTimetables.Items.Refresh();
        BuildWeekMap();
        RebindingEntries();
        RefreshStatus();
    }

    private void BtnAddEntry_Click(object sender, RoutedEventArgs e)
    {
        var tt = SelectedTimetable;
        if (tt == null) return;
        var entry = new CourseEntry
        {
            Time = tt.Entries.Count > 0
                ? ConfigStore.NormalizeTime((ConfigStore.ParseTime(tt.Entries[^1].Time) ?? new TimeSpan(8, 0, 0)).Add(TimeSpan.FromMinutes(45)).ToString())
                : "08:00",
            Subject = _config.Subjects.FirstOrDefault() ?? "",
            AdvanceMinutes = _config.DefaultAdvanceMinutes
        };
        tt.Entries.Add(entry);
        RebindingEntries();
    }

    private void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (GridEntries.SelectedItem is CourseEntry entry && SelectedTimetable is { } tt)
        {
            tt.Entries.Remove(entry);
            RebindingEntries();
        }
    }

    private void BtnApplyDefaultAdvance_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTimetable is not { } tt) return;
        foreach (var entry in tt.Entries)
            entry.AdvanceMinutes = _config.DefaultAdvanceMinutes;
        GridEntries.Items.Refresh();
    }

    // ---------------- 闹钟 ----------------

    private void BtnAddAlarm_Click(object sender, RoutedEventArgs e)
    {
        _config.Alarms.Add(new AlarmItem
        {
            Id = "alarm-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Time = "07:30",
            Label = "新闹钟",
            Enabled = true,
            AdvanceMinutes = 0
        });
        GridAlarms.ItemsSource = null;
        GridAlarms.ItemsSource = _config.Alarms;
    }

    private void BtnDeleteAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (GridAlarms.SelectedItem is AlarmItem alarm)
        {
            _config.Alarms.Remove(alarm);
            GridAlarms.ItemsSource = null;
            GridAlarms.ItemsSource = _config.Alarms;
        }
    }

    private void BtnBrowseRingtone_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择铃声",
            Filter = "音频文件|*.mp3;*.wav|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            _config.RingtonePath = dialog.FileName;
            TxtRingtone.Text = dialog.FileName;
        }
    }

    private void BtnPreviewRingtone_Click(object sender, RoutedEventArgs e)
    {
        _preview?.Stop();
        _preview = new RingPlayer();
        _preview.Play(_config.RingtonePath, TimeSpan.FromSeconds(5));
    }

    private void BtnClearRingtone_Click(object sender, RoutedEventArgs e)
    {
        _config.RingtonePath = "";
        TxtRingtone.Text = "";
    }

    // ---------------- 设置 ----------------

    private void BtnAddSubject_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewSubject.Text.Trim();
        if (name.Length == 0) return;
        if (_config.Subjects.Contains(name))
        {
            MessageBox.Show("该科目已存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _config.Subjects.Add(name);
        TxtNewSubject.Text = "";
        BindAll();
    }

    private void BtnDeleteSubject_Click(object sender, RoutedEventArgs e)
    {
        if (ListSubjects.SelectedItem is not string subject) return;
        _config.Subjects.Remove(subject);
        _config.SeewoAccounts.RemoveAll(a => a.Subject == subject);
        BindAll();
    }

    private void PwdBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && box.DataContext is SeewoAccount account)
            box.Password = account.Password;
    }

    private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && box.DataContext is SeewoAccount account)
            account.Password = box.Password;
    }

    // ---------------- 定时网页 ----------------

    private void BtnAddSmartUrl_Click(object sender, RoutedEventArgs e)
    {
        var item = new SmartUrlItem
        {
            Id = "url-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "新定时网页",
            Url = "https://www.cctv.com/",
            StartTime = "08:00",
            EndTime = "08:45",
            Repeat = "daily",
            WeekStart = 0,
            WeekEnd = 4,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Enabled = true
        };
        _config.SmartUrls.Add(item);
        GridSmartUrls.ItemsSource = null;
        GridSmartUrls.ItemsSource = _config.SmartUrls;
        GridSmartUrls.SelectedItem = item;
    }

    private void BtnDeleteSmartUrl_Click(object sender, RoutedEventArgs e)
    {
        if (GridSmartUrls.SelectedItem is not SmartUrlItem item) return;
        _config.SmartUrls.Remove(item);
        GridSmartUrls.ItemsSource = null;
        GridSmartUrls.ItemsSource = _config.SmartUrls;
    }

    private void BtnTestSmartUrl_Click(object sender, RoutedEventArgs e)
    {
        CommitGrids();
        if (GridSmartUrls.SelectedItem is not SmartUrlItem item)
        {
            MessageBox.Show("请先选中一条定时网页。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !item.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("网址需以 http:// 或 https:// 开头。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Current.SmartUrls.TriggerNow(item);
    }

    private void BtnCloseSmartUrls_Click(object sender, RoutedEventArgs e) => App.Current.SmartUrls.CloseAll();

    private void BtnTestSeewo_Click(object sender, RoutedEventArgs e)
    {
        var account = _config.SeewoAccounts.FirstOrDefault(a => a.HasPassword && !string.IsNullOrWhiteSpace(a.Account));
        if (account == null)
        {
            MessageBox.Show("请先填写任一科目的希沃账号与密码。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _ = new SeewoLoginService().LoginAsync(account.Account, account.Password);
        MessageBox.Show("希沃登录自检已在后台执行（不抢占前台）。\n详细结果请查看日志：" +
                        Path.Combine(AppPaths.LogDir, "seewo.log"),
            "自检", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------- 保存 / 导入 / 导出 ----------------

    private void CommitGrids()
    {
        try
        {
            GridEntries.CommitEdit(DataGridEditingUnit.Cell, true);
            GridEntries.CommitEdit(DataGridEditingUnit.Row, true);
            GridAlarms.CommitEdit(DataGridEditingUnit.Cell, true);
            GridAlarms.CommitEdit(DataGridEditingUnit.Row, true);
            GridSmartUrls.CommitEdit(DataGridEditingUnit.Cell, true);
            GridSmartUrls.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch { }
    }

    private bool ValidateTimes()
    {
        foreach (var tt in _config.Timetables)
        {
            foreach (var entry in tt.Entries)
            {
                if (ConfigStore.ParseTime(entry.Time) == null)
                {
                    MessageBox.Show($"课程表「{tt.Name}」中存在无效时间：{entry.Time}\n请使用 HH:mm 格式（如 08:00）。",
                        "时间格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
        }
        foreach (var alarm in _config.Alarms)
        {
            if (ConfigStore.ParseTime(alarm.Time) == null)
            {
                MessageBox.Show($"闹钟存在无效时间：{alarm.Time}\n请使用 HH:mm 格式（如 07:30）。",
                    "时间格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        if (!int.TryParse(TxtDefaultAdvance.Text.Trim(), out var advance) || advance < 0 || advance > 180)
        {
            MessageBox.Show("默认提前分钟数需为 0 - 180 之间的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        foreach (var item in _config.SmartUrls)
        {
            if (ConfigStore.ParseTime(item.StartTime) == null || ConfigStore.ParseTime(item.EndTime) == null)
            {
                MessageBox.Show($"定时网页「{item.Name}」的开始/结束时间格式不正确，请使用 HH:mm（如 08:00）。",
                    "时间格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (item.Repeat == "once" && !DateTime.TryParse(item.Date, out _))
            {
                MessageBox.Show($"定时网页「{item.Name}」为单次执行，请填写日期（yyyy-MM-dd）。",
                    "日期格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        return true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitGrids();
        if (!ValidateTimes()) return;

        int.TryParse(TxtDefaultAdvance.Text.Trim(), out var advance);
        _config.DefaultAdvanceMinutes = advance;
        _config.RingtonePath = TxtRingtone.Text.Trim();
        _config.AlarmEnabled = ChkAlarmEnabled.IsChecked == true;
        _config.SeewoLoginEnabled = ChkSeewoLogin.IsChecked == true;
        _config.StartWithWindows = ChkAutoStart.IsChecked == true;

        try
        {
            ConfigStore.Save(_config);
            App.Current.ReloadConfig(_config.Clone());
            BindAll();
            MessageBox.Show("设置已保存，调度已生效。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入课程表 JSON",
            Filter = "JSON 文件|*.json|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = ConfigStore.Import(dialog.FileName);
            // 导入课表不改变本机的开机自启设置
            imported.StartWithWindows = _config.StartWithWindows;
            // 课表文件通常不含账号，保留本机已填写的希沃账号
            if (imported.SeewoAccounts.Count == 0)
                imported.SeewoAccounts = _config.SeewoAccounts;
            _config = imported;
            ConfigStore.Save(_config);
            App.Current.ReloadConfig(_config.Clone());
            BindAll();
            MessageBox.Show("课程表已导入并保存。", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        CommitGrids();
        var dialog = new SaveFileDialog
        {
            Title = "导出课程表 JSON",
            Filter = "JSON 文件|*.json",
            FileName = "classmanager-课程表.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ConfigStore.Export(_config, dialog.FileName, includePasswords: false);
            MessageBox.Show("已导出（不含密码明文，符合本地加密存储要求）。\n" + dialog.FileName,
                "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void BtnOpenGuide_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.GuideUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void BtnTestAlarm_Click(object sender, RoutedEventArgs e)
    {
        CommitGrids();
        var tt = ScheduleEngine.GetTimetableForDate(_config, DateTime.Now);
        var subject = tt?.Entries.FirstOrDefault()?.Subject ?? _config.Subjects.FirstOrDefault() ?? "语文";
        App.Current.ShowTestAlarm(subject);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
            return;
        }
        base.OnClosing(e);
    }
}
