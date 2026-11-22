using System.Diagnostics;
using System.Drawing;
using ClassManager.Seewo;

var build = @"C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build";

void Case(string image, string template, double minScore, Action<TemplateMatch, long> check)
{
    using var bmp = new Bitmap(Path.Combine(build, image));
    var sw = Stopwatch.StartNew();
    var m = ImageMatch.Find(bmp, template, minScore);
    sw.Stop();
    Console.WriteLine($"[{image} / {template}] found={m.Found} score={m.Score:0.000} " +
                      $"bounds=({m.Bounds.X},{m.Bounds.Y},{m.Bounds.Width}x{m.Bounds.Height}) scale={m.Scale:0.00} {sw.ElapsedMilliseconds}ms");
    check(m, sw.ElapsedMilliseconds);
}

Case("seewo-login2.png", "login-panel.png", 0.70, (m, _) =>
{
    var a = m.PointAt(0.45, 0.121);
    var p = m.PointAt(0.45, 0.406);
    var b = m.PointAt(0.5, 0.677);
    Console.WriteLine($"   account≈({a.X},{a.Y}) password≈({p.X},{p.Y}) button≈({b.X},{b.Y})  期望≈(1680,418)/(1680,555)/(1828,685)");
});

Case("seewo-filled.png", "login-panel.png", 0.70, (m,_) =>
{
    var b = m.PointAt(0.5, 0.677);
    Console.WriteLine($"   button≈({b.X},{b.Y})  期望≈(1828,685)");
});

Case("seewo-login2.png", "privacy-box.png", 0.72, (m,_) =>
{
    var p = m.PointAt(0.19, 0.51);
    Console.WriteLine($"   checkbox≈({p.X},{p.Y})  期望≈(1373,1063)");
});

Case("seewo-menu.png", "account-menu.png", 0.74, (m,_) =>
{
    var p = m.PointAt(0.23, 0.86);
    Console.WriteLine($"   logout≈({p.X},{p.Y})  期望≈(319,935)");
});
Case("seewo-menu.png", "logout-item.png", 0.74, (m,_) =>
{
    var p = m.PointAt(0.5, 0.5);
    Console.WriteLine($"   logout≈({p.X},{p.Y})  期望≈(255,935)");
});

Case("seewo-home.png", "dialog-ok.png", 0.76, (m,_) =>
{
    var p = m.PointAt(0.5, 0.5);
    Console.WriteLine($"   ok≈({p.X},{p.Y})  期望≈(1521,781)");
});

Console.WriteLine("--- 负样本（应未命中） ---");
Case("seewo-home.png", "login-panel.png", 0.70, (_, _) => { });
Case("seewo-login2.png", "logout-item.png", 0.74, (_, _) => { });
