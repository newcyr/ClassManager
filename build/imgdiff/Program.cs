using System.Drawing;
using ClassManager.Seewo;
var build = @"C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build";
var tplDir = @"C:\Users\admin\Documents\Trae-CN\temp_20261120_01\assets\templates";
var screen = new Bitmap(Path.Combine(build, "seewo-login2.png"));
Console.WriteLine(ImageMatch.DebugNccVerbose(screen, "login-panel.png", 1360, 360, 15));
Console.WriteLine("--- 屏幕与自身对比 ---");
Console.WriteLine(ImageMatch.DebugNccVerbose(new Bitmap(Path.Combine(tplDir, "login-panel.png")), "login-panel.png", 0, 0, 15));

