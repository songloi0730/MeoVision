Spike giao diện MeoVision v2.1 (2026-10-16) — nhúng control VisionPro THẬT vào bố cục mockup.
Không phải code của MeoVision; giữ làm bằng chứng cho DESIGN_LOG [16] và Chương 22.

Biên dịch (Roslyn csc của VS2022, net48 x64), tham chiếu mọi Cognex.VisionPro*.dll trong
%VPRO_ROOT%\ReferencedAssemblies (trừ 3D) + System.Windows.Forms + System.Drawing:
  csc /target:winexe /platform:x64 /langversion:7.3 /out:UiSpike.exe /r:... UiSpike.cs
Chạy:  UiSpike.exe <main|main2|main3|tbedit|tbedit2|tbedit3|qbedit|calib|acq> <thư mục ảnh>
Mỗi cảnh tự chụp cửa sổ (PrintWindow) rồi thoát; log ở <cảnh>_log.txt.

shots/: main (CogRecordsDisplay mặc định), main2 (bỏ combobox record + thanh cuộn, dock 360),
tbedit (CogToolBlockEditV2, font 9.75 thừa hưởng → chồng chữ), tbedit3 (font mặc định + ẩn 5 nút),
qbedit (CogJobManagerEdit = QuickBuild nhúng), calib (CogCalibNPointToNPointEditV2), acq (CogAcqFifoEditV2),
appwizard_run (ứng dụng do Application Wizard của VisionPro sinh từ mẫu QuickBuild2Jobs, Run Once).
