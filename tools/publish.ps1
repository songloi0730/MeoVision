# -------------------------------------------------------
# File:    publish.ps1
# Purpose: Đóng gói một bản phát hành MeoVision (Chương 24, mục 24.2):
#          build Release -> chép ra dist\MeoVision-<phiên bản>\ -> exe tự ghi release.json.
# Dùng:    powershell -ExecutionPolicy Bypass -File tools\publish.ps1
# -------------------------------------------------------
param(
    # Hai số đầu chỉ đổi khi chủ dự án quyết định; số cuối = ngày build YYMMDD.
    [string]$Major = "2.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$version = "$Major.$(Get-Date -Format yyMMdd)"
$vs = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$msbuild = "$vs\MSBuild\Current\Bin\amd64\MSBuild.exe"

# Ngày build chỉ vào InformationalVersion: mỗi phần của AssemblyVersion/FileVersion <= 65535,
# "2.0.261029" làm build lỗi CS7034 (đo thật).
& $msbuild "$root\MeoVision.sln" /t:Rebuild /p:Configuration=Release `
    /p:InformationalVersion=$version -v:minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$output = "$root\src\MeoVision.UI\bin\Release\net48"
$target = "$root\dist\MeoVision-$version"
# Dữ liệu trạm nằm TRONG thư mục bản phát hành (DESIGN_LOG [26]): đóng gói lại cùng ngày không được
# xoá nó — chuyển sang một bên rồi trả lại. Thư mục Data trong bin\Release (chạy thử) không chép theo.
$kept = $null
if (Test-Path $target) {
    if (Test-Path "$target\Data") {
        $kept = "$root\dist\Data-kept-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Move-Item "$target\Data" $kept
    }
    Remove-Item $target -Recurse -Force
}
New-Item -ItemType Directory $target | Out-Null
Copy-Item "$output\*" $target -Recurse -Exclude Data
if ($kept) { Move-Item $kept "$target\Data" }

# Exe giao diện: Start-Process -Wait mới chờ nó chạy xong và lấy được mã thoát.
$p = Start-Process "$target\MeoVision.exe" -ArgumentList "--write-release" -Wait -PassThru `
    -RedirectStandardOutput "$target\..\write-release.log" -NoNewWindow
if ($p.ExitCode -ne 0) { throw "--write-release failed ($($p.ExitCode))" }
Get-Content "$target\..\write-release.log"
Write-Host "Ban phat hanh: $target"
