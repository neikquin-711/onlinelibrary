# OnLibrary

thuvienonline

Website thư viện trực tuyến xây dựng bằng ASP.NET Core MVC (.NET 9), SQL Server/EF Core, cookie authentication, giao diện tiếng Việt và responsive.

## Chạy ứng dụng

```powershell


```

Mở địa chỉ localhost được hiển thị trong terminal.

Mặc định ứng dụng dùng SQLite và tự tạo `App_Data/onlibrary.db`, nên không cần cài database server. Để dùng SQL Server, đặt `ONLIBRARY_DB` thành connection string trước khi chạy. Không commit connection string có mật khẩu vào mã nguồn.

Để khởi tạo tài khoản quản trị lần đầu, đặt hai biến môi trường `ONLIBRARY_ADMIN_EMAIL` và `ONLIBRARY_ADMIN_PASSWORD` trước lần chạy đầu tiên. Ứng dụng chỉ tạo tài khoản này khi chưa tồn tại; mật khẩu được băm trước khi lưu. Đăng ký công khai chỉ tạo vai trò Độc giả.

Upload yêu cầu ClamAV cài trên máy chủ. Đặt `ONLIBRARY_CLAMSCAN_PATH` thành đường dẫn chương trình `clamscan` (ví dụ `C:\\Program Files\\ClamAV\\clamscan.exe`) trước khi chạy. Nếu chưa cấu hình scanner hoặc scanner không báo sạch, ứng dụng từ chối lưu tệp. Tệp được giới hạn 25 MB, kiểm tra phần mở rộng, MIME và chữ ký/cấu trúc, sau đó đặt tên ngẫu nhiên và lưu trong `App_Data/documents` ngoài `wwwroot`. Có thể đổi thư mục gốc bằng cách triển khai `DocumentStoragePath` theo cấu hình môi trường.

Khóa cookie và anti-forgery được lưu trong `App_Data/data-protection-keys-v2` và bị loại khỏi Git cùng dữ liệu cục bộ. Trên máy chủ Windows, có thể bật mã hóa DPAPI bằng `ONLIBRARY_PROTECT_KEYS_WITH_DPAPI=true`.

## Các trang hiện có

- `/` — trang chủ và sách được tuyển chọn.
- `/Home/Catalog` — tìm kiếm, lọc danh mục, sắp xếp tài liệu.
- `/Home/Details/1` — thông tin tài liệu.
- `/Home/Read/1` — giao diện đọc mẫu.
- `/Home/Admin` — bảng quản lý tài liệu (Admin/Thủ thư).
- `/Home/Create` — thêm tài liệu; biểu mẫu POST có anti-forgery token (Admin/Thủ thư).
- `/Home/Edit/1` — sửa metadata hoặc thay tệp có quét bảo mật (Admin/Thủ thư).
- `/Home/Users` — quản lý vai trò độc giả và thủ thư (Admin).
- `/Home/Reports` — hàng đợi báo cáo tài liệu (Admin/Thủ thư).
- `/Home/ReviewModeration` — ẩn/hiện nhận xét (Admin/Thủ thư).
- `/Home/DocumentFile/1` — xem/tải tệp qua controller; không lộ đường dẫn lưu trữ.
- `/Home/MyLibrary` — danh sách yêu thích và lịch sử đọc của tài khoản (đăng nhập).
- `/Account/Register` và `/Account/Login` — tài khoản độc giả.

## Trạng thái dữ liệu

Dữ liệu danh mục, tài khoản, yêu thích, đánh giá, lịch sử đọc, lịch sử tải xuống và báo cáo lưu trong cơ sở dữ liệu. Độc giả đã đăng nhập có thể lưu/xóa yêu thích, gửi đánh giá (mỗi người một đánh giá cho mỗi tài liệu, có thể sửa bằng cách gửi lại), xem lịch sử đọc/tải xuống và báo cáo tài liệu. Admin/Thủ thư có thể xử lý/từ chối báo cáo, ẩn/hiện nhận xét và phân trang kết quả tra cứu. Bảng quản trị hiển thị số liệu đọc, tải xuống và báo cáo từ dữ liệu thực. Tải lên PDF, EPUB và DOCX đã được nối vào luồng tạo tài liệu; PDF mở bằng trình xem tích hợp của trình duyệt, EPUB/DOCX được tải về. Phân quyền dùng cookie authentication với các vai trò Admin, Librarian và Reader. Admin có thể cấp hoặc thu hồi vai trò Thủ thư trong trang Người dùng; tài khoản Admin không thể bị đổi vai trò tại trang này.
