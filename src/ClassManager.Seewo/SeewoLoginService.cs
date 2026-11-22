using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Automation;
using ClassManager.Core;

namespace ClassManager.Seewo;

/// <summary>
/// 希沃（Seewo）自动登录：检测登录状态 → 必要时先退出上一账号 → 输入当堂科目账号密码 → 检测结果。
/// 全流程静默：任何失败只写日志，不弹窗、不抛异常、不影响全屏提醒。
/// </summary>
public sealed class SeewoLoginService
{
    private const int LoginPanelWaitMs = 25000;
    private const int LoginResultWaitMs = 20000;

    public async Task<bool> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            AppPaths.Log("seewo", "未配置账号或密码，跳过自动登录。");
            return false;
        }

        try
        {
            var exe = SeewoLocator.FindExecutable();
            AppPaths.Log("seewo", exe == null ? "未在本机找到希沃客户端，跳过自动登录。" : "希沃程序：" + exe);
            if (exe == null) return false;

            var process = FindRunningProcess() ?? StartProcess(exe);
            if (process == null)
            {
                AppPaths.Log("seewo", "启动希沃失败。");
                return false;
            }

            var hwnd = await WaitForMainWindowAsync(process, ct).ConfigureAwait(false);
            if (hwnd == IntPtr.Zero)
            {
                AppPaths.Log("seewo", "等待希沃窗口超时，重启客户端后继续登录。");
                var restarted = await RestartClientAsync(exe, ct).ConfigureAwait(false);
                if (restarted.process == null || restarted.hwnd == IntPtr.Zero)
                {
                    AppPaths.Log("seewo", "重启后仍无可用希沃窗口。");
                    return false;
                }
                process = restarted.process;
                hwnd = restarted.hwnd;
            }

            UiaHelper.ShowWindow(hwnd, 9); // SW_RESTORE
            UiaHelper.SetForegroundWindow(hwnd);
            await Task.Delay(1200, ct).ConfigureAwait(false);

            // 先关掉"程序意外退出，正在恢复课件"等遮挡弹窗（按关键图标识别，不依赖分辨率）
            TryDismissRecoveryDialog(process, hwnd);

            var root = UiaHelper.ElementFromHandle(hwnd);
            if (root == null)
            {
                AppPaths.Log("seewo", "无法获取希沃窗口的 UIA 根元素。");
                return false;
            }

            var logoutAttempted = false;
            if (IsLoggedIn(root) || FindLogoutTarget(process, hwnd) != null)
            {
                logoutAttempted = true;
                AppPaths.Log("seewo", "检测到已有账号登录，执行退出登录，随后继续登录当前账号。");
                var loggedOut = await LogoutAsync(process, hwnd, ct).ConfigureAwait(false);
                AppPaths.Log("seewo", loggedOut ? "已退出上一账号，继续登录。" : "未能确认退出状态，继续尝试登录。");
                await Task.Delay(1500, ct).ConfigureAwait(false);
                root = UiaHelper.ElementFromHandle(hwnd) ?? root;
            }

            var panelReady = await WaitForLoginPanelAnyAsync(process, hwnd, 12, ct).ConfigureAwait(false);
            if (!panelReady && logoutAttempted)
            {
                // 退出后可能停在空白页或需要重启客户端才显示登录框；重启后继续同一次登录
                AppPaths.Log("seewo", "退出后未出现登录页，重启希沃客户端继续登录。");
                var restarted = await RestartClientAsync(exe, ct).ConfigureAwait(false);
                if (restarted.process != null && restarted.hwnd != IntPtr.Zero)
                {
                    process = restarted.process;
                    hwnd = restarted.hwnd;
                    root = UiaHelper.ElementFromHandle(hwnd) ?? root;
                    panelReady = await WaitForLoginPanelAnyAsync(process, hwnd, 30, ct).ConfigureAwait(false);
                    AppPaths.Log("seewo", panelReady ? "已重新打开登录页，继续登录。" : "重启后仍未识别登录页，继续键盘输入。");
                }
            }
            if (!panelReady)
                AppPaths.Log("seewo", "未找到登录面板，尝试直接键盘输入。");

            if (!FillCredentials(process, hwnd, account, password))
            {
                AppPaths.Log("seewo", "填写账号密码失败。");
                return false;
            }

            EnsurePrivacyAgreed(process, hwnd);
            SubmitLogin(process, hwnd);
            var ok = await WaitForLoginResultAsync(process, hwnd, ct).ConfigureAwait(false);
            if (!ok)
            {
                AppPaths.Log("seewo", $"账号 {MaskAccount(account)} 首次登录未确认，重启客户端后重试一次。");
                var retry = await RestartClientAsync(exe, ct).ConfigureAwait(false);
                if (retry.process != null && retry.hwnd != IntPtr.Zero)
                {
                    process = retry.process;
                    hwnd = retry.hwnd;
                    await WaitForLoginPanelAnyAsync(process, hwnd, 30, ct).ConfigureAwait(false);
                    if (FillCredentials(process, hwnd, account, password))
                    {
                        EnsurePrivacyAgreed(process, hwnd);
                        SubmitLogin(process, hwnd);
                        ok = await WaitForLoginResultAsync(process, hwnd, ct).ConfigureAwait(false);
                    }
                }
            }
            AppPaths.Log("seewo", ok ? $"账号 {MaskAccount(account)} 登录成功。" : $"账号 {MaskAccount(account)} 登录结果未确认（已静默处理）。");
            return ok;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "自动登录异常（已静默）：" + ex.Message);
            return false;
        }
    }

    private static string MaskAccount(string account) =>
        account.Length <= 2 ? "**" : account.Substring(0, 1) + "***" + account.Substring(account.Length - 1);

    private static Process? FindRunningProcess()
    {
        foreach (var name in SeewoLocator.KnownExeNames.Select(n => Path.GetFileNameWithoutExtension(n)))
        {
            try
            {
                var candidates = Process.GetProcessesByName(name).ToList();
                // 优先选择真正有主窗口的进程（希沃会同时存在无窗口的辅助进程）
                var withWindow = candidates.FirstOrDefault(p =>
                {
                    try { p.Refresh(); return p.MainWindowHandle != IntPtr.Zero; }
                    catch { return false; }
                });
                if (withWindow != null) return withWindow;
                if (candidates.Count > 0) return candidates[0];
            }
            catch { }
        }
        try
        {
            return Process.GetProcesses()
                .FirstOrDefault(p =>
                {
                    try
                    {
                        var f = p.MainModule?.FileName ?? "";
                        return f.Contains("EasiNote", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });
        }
        catch { return null; }
    }

    private static Process? StartProcess(string exe)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "启动进程失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>结束全部希沃进程并重新启动，用于退出后未出现登录页或登录未确认时继续登录。</summary>
    private static async Task<(Process? process, IntPtr hwnd)> RestartClientAsync(string exe, CancellationToken ct)
    {
        try
        {
            foreach (var name in SeewoLocator.KnownExeNames.Select(n => Path.GetFileNameWithoutExtension(n)))
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); } catch { }
                    finally { p.Dispose(); }
                }
            }
            await Task.Delay(1800, ct).ConfigureAwait(false);

            var process = StartProcess(exe);
            if (process == null) return (null, IntPtr.Zero);

            var hwnd = await WaitForMainWindowAsync(process, ct).ConfigureAwait(false);
            if (hwnd == IntPtr.Zero) return (process, IntPtr.Zero);

            UiaHelper.ShowWindow(hwnd, 9);
            UiaHelper.SetForegroundWindow(hwnd);
            await Task.Delay(1500, ct).ConfigureAwait(false);
            return (process, hwnd);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "重启希沃异常（已静默）：" + ex.Message);
            return (null, IntPtr.Zero);
        }
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddMilliseconds(LoginPanelWaitMs);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var hwnd = FindUsableWindow(process);
            if (hwnd != IntPtr.Zero) return hwnd;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return IntPtr.Zero;
    }

    /// <summary>主窗口句柄不可用时，退而寻找该进程可见且尺寸正常的窗口（退出后登录窗常不属于主窗口）。</summary>
    private static IntPtr FindUsableWindow(Process process)
    {
        try
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
        }
        catch { }
        try
        {
            foreach (var h in UiaHelper.GetVisibleWindows(process.Id))
            {
                if (UiaHelper.GetWindowRect(h, out var rect) &&
                    rect.Right - rect.Left >= 300 && rect.Bottom - rect.Top >= 200)
                    return h;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private static readonly string[] LogoutNames =
        { "退出登录", "退出帐号", "退出账号", "切换账号", "切换帐号", "注销", "登出" };

    /// <summary>通过账号头像按钮 / "退出登录 / 切换账号" 等元素判断是否已登录。</summary>
    private static bool IsLoggedIn(AutomationElement root)
    {
        return UiaHelper.FindByAutomationId(root, "AccountProfileButton") != null ||
               UiaHelper.FindNamed(root, LogoutNames) != null;
    }

    /// <summary>枚举该进程（含同系列辅助进程）当前所有可见窗口的 UIA 根元素。</summary>
    private static List<AutomationElement> GetRoots(Process process, IntPtr mainHwnd)
    {
        var roots = new List<AutomationElement>();
        var handles = new List<IntPtr>();
        var pids = new HashSet<int> { process.Id };
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("EasiNote", StringComparison.OrdinalIgnoreCase))
                        pids.Add(p.Id);
                }
                catch { }
            }
        }
        catch { }

        foreach (var pid in pids)
        {
            try { handles.AddRange(UiaHelper.GetVisibleWindows(pid)); } catch { }
        }
        if (mainHwnd != IntPtr.Zero && !handles.Contains(mainHwnd)) handles.Insert(0, mainHwnd);

        foreach (var h in handles)
        {
            var el = UiaHelper.ElementFromHandle(h);
            if (el != null) roots.Add(el);
        }
        return roots;
    }

    /// <summary>在全部窗口里查找可见的退出登录入口（优先"退出登录"菜单项）。</summary>
    private static AutomationElement? FindLogoutTarget(Process process, IntPtr hwnd)
    {
        AutomationElement? best = null;
        var bestScore = int.MaxValue;

        foreach (var root in GetRoots(process, hwnd))
        {
            foreach (var el in UiaHelper.Descendants(root))
            {
                try
                {
                    var name = el.Current.Name ?? "";
                    if (!LogoutNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                    if (el.Current.IsOffscreen) continue;
                    var rect = el.Current.BoundingRectangle;
                    if (rect.IsEmpty || rect.Width <= 1 || rect.Height <= 1) continue;

                    var score = name.Equals("退出登录", StringComparison.Ordinal) ? 0
                              : name.Contains("退出登录", StringComparison.Ordinal) ? 1
                              : el.Current.ControlType == ControlType.MenuItem ? 2
                              : 3;
                    if (score < bestScore)
                    {
                        best = el;
                        bestScore = score;
                    }
                }
                catch { }
            }
        }
        return best;
    }

    /// <summary>枚举该进程（含同系列辅助进程）的可见窗口句柄。</summary>
    private static IEnumerable<IntPtr> CandidateHandles(Process process, IntPtr mainHwnd)
    {
        var seen = new HashSet<IntPtr>();
        if (mainHwnd != IntPtr.Zero && seen.Add(mainHwnd)) yield return mainHwnd;

        var pids = new HashSet<int> { process.Id };
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("EasiNote", StringComparison.OrdinalIgnoreCase))
                        pids.Add(p.Id);
                }
                catch { }
            }
        }
        catch { }

        foreach (var pid in pids)
        {
            List<IntPtr> handles;
            try { handles = UiaHelper.GetVisibleWindows(pid); } catch { continue; }
            foreach (var h in handles)
                if (seen.Add(h)) yield return h;
        }
    }

    /// <summary>在所有可见窗口里做多尺度模板匹配（坐标为屏幕绝对坐标）。</summary>
    private static TemplateMatch FindTemplate(Process process, IntPtr hwnd, string templateName, double minScore)
    {
        foreach (var h in CandidateHandles(process, hwnd))
        {
            var bmp = ImageMatch.CaptureWindow(h, out var ox, out var oy);
            if (bmp == null) continue;
            try
            {
                var match = ImageMatch.Find(bmp, templateName, minScore, ox, oy);
                if (match.Found) return match;
            }
            catch { }
        }
        return new TemplateMatch { Found = false };
    }

    /// <summary>按关键图标识别并关闭"程序意外退出，正在恢复课件"等遮挡弹窗。</summary>
    private static bool TryDismissRecoveryDialog(Process process, IntPtr hwnd)
    {
        try
        {
            var match = FindTemplate(process, hwnd, "dialog-ok.png", 0.76);
            if (!match.Found) return false;

            var point = match.PointAt(0.5, 0.5);
            UiaHelper.SetForegroundWindow(hwnd);
            UiaHelper.ClickAt(point.X, point.Y);
            Thread.Sleep(700);
            AppPaths.Log("seewo", $"图标识别：已关闭提示弹窗（score={match.Score:0.00}）。");
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "关闭提示弹窗异常（已静默）：" + ex.Message);
            return false;
        }
    }

    /// <summary>退出上一账号：找入口 → 点击 → 处理确认弹窗 → 等待回到登录页；失败自动重试一次。</summary>
    private static async Task<bool> LogoutAsync(Process process, IntPtr hwnd, CancellationToken ct)
    {
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var target = FindLogoutTarget(process, hwnd);
                if (target == null && TryOpenAccountMenu(process, hwnd))
                {
                    await Task.Delay(900, ct).ConfigureAwait(false);
                    target = FindLogoutTarget(process, hwnd);
                }
                if (target == null)
                {
                    // 图标兜底：按窗口相对比例点击左上角账号头像，展开账号菜单
                    var avatar = UiaHelper.RelativePoint(hwnd, 0.039, 0.140);
                    if (avatar.HasValue)
                    {
                        UiaHelper.ClickAt(avatar.Value.X, avatar.Value.Y);
                        AppPaths.Log("seewo", "按头像相对位置展开账号菜单（图标兜底）。");
                        await Task.Delay(1100, ct).ConfigureAwait(false);
                        target = FindLogoutTarget(process, hwnd);
                    }
                }

                if (target == null)
                {
                    // 再兜底：模板匹配"退出登录"菜单项
                    var menuItem = FindTemplate(process, hwnd, "logout-item.png", 0.80);
                    if (menuItem.Found)
                    {
                        var p = menuItem.PointAt(0.5, 0.5);
                        UiaHelper.ClickAt(p.X, p.Y);
                        AppPaths.Log("seewo", $"图标识别：已点击退出登录（score={menuItem.Score:0.00}）。");
                        await Task.Delay(900, ct).ConfigureAwait(false);
                        await ConfirmLogoutAsync(process, hwnd, ct).ConfigureAwait(false);
                        if (await WaitForLoginPanelAnyAsync(process, hwnd, 12, ct).ConfigureAwait(false))
                            return true;
                        continue;
                    }

                    AppPaths.Log("seewo", "未找到退出登录入口。");
                    return false;
                }

                UiaHelper.SetForegroundWindow(hwnd);
                var clicked = UiaHelper.TryInvoke(target);
                AppPaths.Log("seewo", $"点击退出登录入口：{clicked}（第 {attempt} 次）。");
                if (!clicked) return false;

                await Task.Delay(900, ct).ConfigureAwait(false);
                await ConfirmLogoutAsync(process, hwnd, ct).ConfigureAwait(false);

                if (await WaitForLoginPanelAnyAsync(process, hwnd, 12, ct).ConfigureAwait(false))
                    return true;

                AppPaths.Log("seewo", $"第 {attempt} 次退出后仍未出现登录框，重试。");
                await Task.Delay(800, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "退出登录异常（已静默）：" + ex.Message);
        }
        return false;
    }

    /// <summary>退出时的"确定/确认"弹窗，逐个窗口查找并点击。</summary>
    private static async Task ConfirmLogoutAsync(Process process, IntPtr hwnd, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(4);
        while (DateTime.Now < deadline)
        {
            foreach (var root in GetRoots(process, hwnd))
            {
                var button = UiaHelper.Descendants(root, ControlType.Button).FirstOrDefault(b =>
                {
                    try
                    {
                        var name = b.Current.Name ?? "";
                        return name.Equals("确定", StringComparison.Ordinal) ||
                               name.Equals("确认", StringComparison.Ordinal) ||
                               name.Equals("是", StringComparison.Ordinal) ||
                               name.Equals("退出", StringComparison.Ordinal) ||
                               name.Contains("退出登录", StringComparison.Ordinal) ||
                               name.Contains("退出账号", StringComparison.Ordinal) ||
                               name.Contains("退出帐号", StringComparison.Ordinal);
                    }
                    catch { return false; }
                });
                if (button != null && UiaHelper.TryInvoke(button))
                {
                    AppPaths.Log("seewo", "已点击退出确认弹窗。");
                    await Task.Delay(600, ct).ConfigureAwait(false);
                    return;
                }
            }
            await Task.Delay(400, ct).ConfigureAwait(false);
        }
    }

    /// <summary>直接找不到退出入口时，点击账号头像打开账号菜单（希沃白板为 AccountProfileButton）。</summary>
    private static bool TryOpenAccountMenu(Process process, IntPtr hwnd)
    {
        string[] keys = { "头像", "个人中心", "账号信息", "帐号信息", "我的账号", "我的帐号" };
        foreach (var root in GetRoots(process, hwnd))
        {
            var btn = UiaHelper.FindByAutomationId(root, "AccountProfileButton");
            if (btn != null && UiaHelper.TryInvoke(btn))
            {
                AppPaths.Log("seewo", "已点击账号头像，展开账号菜单。");
                return true;
            }

            var el = UiaHelper.FindNamed(root, keys);
            if (el != null && UiaHelper.TryInvoke(el))
            {
                AppPaths.Log("seewo", "已尝试打开账号菜单。");
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> WaitForLoginPanelAsync(AutomationElement root, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var edits = UiaHelper.Descendants(root, ControlType.Edit).ToList();
            if (edits.Count >= 2) return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>在全部窗口里等待登录框（账号/密码两个输入框）出现。</summary>
    private static async Task<bool> WaitForLoginPanelAnyAsync(Process process, IntPtr hwnd, int seconds, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var root in GetRoots(process, hwnd))
            {
                if (UiaHelper.Descendants(root, ControlType.Edit).Count() >= 2) return true;
            }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static bool FillCredentials(Process process, IntPtr hwnd, string account, string password)
    {
        try
        {
            var edits = GetRoots(process, hwnd)
                .SelectMany(r => UiaHelper.Descendants(r, ControlType.Edit))
                .Where(e =>
                {
                    try
                    {
                        var id = e.Current.AutomationId ?? "";
                        return !e.Current.IsOffscreen &&
                               id.IndexOf("search", StringComparison.OrdinalIgnoreCase) < 0 &&
                               id.IndexOf("搜索", StringComparison.Ordinal) < 0;
                    }
                    catch { return true; }
                })
                .ToList();

            if (edits.Count >= 2)
            {
                var passwordEdit = edits.FirstOrDefault(e =>
                {
                    try { return e.Current.IsPassword; }
                    catch { return false; }
                });

                var userEdit = edits.FirstOrDefault(e => !ReferenceEquals(e, passwordEdit)) ?? edits[0];
                passwordEdit ??= edits[1];

                var okUser = UiaHelper.TrySetValue(userEdit, account);
                var okPwd = UiaHelper.TrySetValue(passwordEdit, password);
                AppPaths.Log("seewo", $"UIA 填表结果：账号={okUser}，密码={okPwd}。");
                if (okUser && okPwd) return true;
            }

            // 图标兜底：按"登录表单"模板定位账号框 / 密码框（相对比例，自动适配缩放）
            var panel = FindTemplate(process, hwnd, "login-panel.png", 0.70);
            if (panel.Found)
            {
                var accountPoint = panel.PointAt(0.45, 0.121);
                var passwordPoint = panel.PointAt(0.45, 0.406);

                UiaHelper.SetForegroundWindow(hwnd);
                UiaHelper.ClickAt(accountPoint.X, accountPoint.Y);
                Thread.Sleep(250);
                System.Windows.Forms.SendKeys.SendWait("^a");
                System.Windows.Forms.SendKeys.SendWait(UiaHelper.Escape(account));

                UiaHelper.ClickAt(passwordPoint.X, passwordPoint.Y);
                Thread.Sleep(250);
                System.Windows.Forms.SendKeys.SendWait("^a");
                System.Windows.Forms.SendKeys.SendWait(UiaHelper.Escape(password));

                AppPaths.Log("seewo", $"图标识别：已按登录表单定位并填入账号密码（score={panel.Score:0.00}）。");
                return true;
            }

            // 兜底：聚焦窗口后纯键盘输入
            AppPaths.Log("seewo", "使用键盘兜底方式输入账号密码。");
            UiaHelper.SetForegroundWindow(hwnd);
            Thread.Sleep(500);
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Windows.Forms.SendKeys.SendWait(UiaHelper.Escape(account));
            UiaHelper.SendKey(0x09); // TAB
            Thread.Sleep(250);
            System.Windows.Forms.SendKeys.SendWait(UiaHelper.Escape(password));
            Thread.Sleep(250);
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "填表异常：" + ex.Message);
            return false;
        }
    }

    /// <summary>勾选登录面板下方的"已阅读并同意用户协议/隐私政策"复选框（已勾选则保持不动）。</summary>
    private static void EnsurePrivacyAgreed(Process process, IntPtr hwnd)
    {
        try
        {
            var roots = GetRoots(process, hwnd);
            var boxes = roots.SelectMany(r => UiaHelper.Descendants(r, ControlType.CheckBox)).ToList();
            var box = boxes.FirstOrDefault(b =>
            {
                try
                {
                    var name = b.Current.Name ?? "";
                    return name.Contains("同意") || name.Contains("隐私") || name.Contains("协议") || name.Contains("阅读");
                }
                catch { return false; }
            });
            box ??= boxes.Count == 1 ? boxes[0] : null;

            if (box != null)
            {
                var on = TryToggle(box);
                AppPaths.Log("seewo", on ? "已勾选用户协议/隐私政策复选框。" : "已点击用户协议复选框（状态未确认）。");
                return;
            }

            // 图标兜底：隐私复选框在 UIA 中不可见，用模板匹配定位并点击
            var privacy = FindTemplate(process, hwnd, "privacy-box.png", 0.80);
            if (privacy.Found)
            {
                var point = privacy.PointAt(0.19, 0.51);
                UiaHelper.ClickAt(point.X, point.Y);
                AppPaths.Log("seewo", $"图标识别：已勾选隐私协议复选框（score={privacy.Score:0.00}）。");
                return;
            }

            // 兜底：部分版本的复选框控件类型不是 CheckBox，直接点击协议文字
            AutomationElement? text = null;
            foreach (var r in roots)
            {
                text = UiaHelper.FindNamed(r, "我已阅读并同意", "用户协议", "隐私政策", "同意");
                if (text != null) break;
            }
            if (text != null && UiaHelper.TryInvoke(text))
                AppPaths.Log("seewo", "已通过点击协议文字完成勾选。");
            else
                AppPaths.Log("seewo", "未找到用户协议勾选框（可能无需勾选）。");
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "勾选用户协议异常（已静默）：" + ex.Message);
        }
    }

    /// <summary>确保复选框为选中状态；返回操作后是否为选中。</summary>
    private static bool TryToggle(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var p))
            {
                var toggle = (TogglePattern)p;
                if (toggle.Current.ToggleState == ToggleState.On) return true;
                toggle.Toggle();
                Thread.Sleep(300);
                return toggle.Current.ToggleState == ToggleState.On;
            }
        }
        catch { }

        if (!UiaHelper.TryInvoke(el)) return false;
        Thread.Sleep(300);
        try
        {
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var p2))
                return ((TogglePattern)p2).Current.ToggleState == ToggleState.On;
        }
        catch { }
        return false;
    }

    private static void SubmitLogin(Process process, IntPtr hwnd)
    {
        try
        {
            var button = GetRoots(process, hwnd)
                .SelectMany(r => UiaHelper.Descendants(r, ControlType.Button))
                .FirstOrDefault(b =>
                {
                    try
                    {
                        var name = b.Current.Name ?? "";
                        return !b.Current.IsOffscreen &&
                               (name.Equals("登录", StringComparison.Ordinal) || name.Contains("登录"));
                    }
                    catch { return false; }
                });

            if (button != null && UiaHelper.TryInvoke(button))
            {
                AppPaths.Log("seewo", "已点击登录按钮。");
                return;
            }

            // 图标兜底：按登录表单模板点击绿色"登录"按钮
            var panel = FindTemplate(process, hwnd, "login-panel.png", 0.70);
            if (panel.Found)
            {
                var point = panel.PointAt(0.5, 0.677);
                UiaHelper.ClickAt(point.X, point.Y);
                AppPaths.Log("seewo", $"图标识别：已点击登录按钮（score={panel.Score:0.00}）。");
                return;
            }

            UiaHelper.SetForegroundWindow(hwnd);
            UiaHelper.SendKey(0x0D); // ENTER
            AppPaths.Log("seewo", "已按回车提交登录。");
        }
        catch (Exception ex)
        {
            AppPaths.Log("seewo", "提交登录异常：" + ex.Message);
        }
    }

    private static async Task<bool> WaitForLoginResultAsync(Process process, IntPtr hwnd, CancellationToken ct)
    {
        var deadline = DateTime.Now.AddMilliseconds(LoginResultWaitMs);
        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var root in GetRoots(process, hwnd))
            {
                if (UiaHelper.FindByAutomationId(root, "AccountProfileButton") != null) return true;
            }
            if (FindLogoutTarget(process, hwnd) != null) return true;
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        return false;
    }
}
