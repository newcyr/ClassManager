# 从真实截图裁剪希沃关键图标模板（用于多尺度模板匹配）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$build = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\build'
$out = 'C:\Users\admin\Documents\Trae-CN\temp_20261120_01\assets\templates'
New-Item -ItemType Directory -Force $out | Out-Null

function Crop($src, $x, $y, $w, $h, $name) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $build $src))
    $rect = New-Object System.Drawing.Rectangle $x, $y, $w, $h
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $img.Dispose()
    Write-Host ("{0}  <- {1} {2},{3} {4}x{5}" -f $name, $src, $x, $y, $w, $h)
}

# 登录页（2264x1319）：整块账号/密码/登录按钮表单（锚点）
Crop 'seewo-login2.png' 1360 360 760 480 'login-panel.png'
# 登录页：隐私协议复选框 + “已读”
Crop 'seewo-login2.png' 1342 1030 120 65 'privacy-box.png'
# 账号菜单展开（2588x1520）：退出登录菜单项
Crop 'seewo-menu.png' 250 905 250 70 'logout-item.png'
# 恢复课件弹窗的确定按钮
Crop 'seewo-home.png' 1420 730 200 90 'dialog-ok.png'
