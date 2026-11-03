# vppinspect — đọc TĨNH file job VisionPro (.vpp / .vtt)

Đọc file job **mà không nạp vào .NET/VisionPro**: không giải tuần tự kiểu lạ, không biên dịch script.
Dùng để xem job của người khác (repo công khai, job nhận từ nhà tích hợp) trước khi quyết định có nạp
hay không. Sách Ch26 (§26.2) giải thích vì sao.

## Chạy

```bat
python -I vppinspect.py <thư mục hoặc file .vpp> <thư mục báo cáo>
```

Ra thư mục báo cáo: mỗi job một `*.md` (cây tool + tham số chính, terminal, link, script) và
`*.scriptN.cs.txt` (nguyên văn script — văn bản, không biên dịch), cộng `_summary.csv` cho cả thư mục.

## Định dạng hỗ trợ

| Byte đầu | Định dạng | Bộ đọc |
|---|---|---|
| `00 01 00 00 00 FF FF FF FF` | BinaryFormatter (MS-NRBF) | `nrbf.py` |
| `<SOAP-ENV:Envelope` | SOAP XML | `vppinspect.SoapGraph` (từ chối DOCTYPE/ENTITY) |
| `<?xml … ProgID="Cognex…"` | XML đời COM | `vppinspect.comxml_report` |

Đã kiểm (2026-11-04): 3 job mẫu `samples/toolchains/jobs`, 114 file mẫu của VisionPro 9.0 CR2 và 224
file tải từ GitHub — đọc được hết, không lỗi.

## Giới hạn

- Không chạy job: không có record, không có kết quả trên ảnh.
- `byte[]` lớn (ảnh train, dữ liệu tool native) chỉ ghi kích thước, không giữ nội dung.
- Tham số chỉ in các giá trị vô hướng (số, bool, chuỗi ngắn, enum); đối tượng con (vùng) in tên lớp.
- "Script trông vô hại" KHÔNG làm việc nạp file nhị phân thành an toàn — bước dựng đối tượng có rủi ro
  riêng. Nạp job lạ chỉ trên máy thử cách ly.
