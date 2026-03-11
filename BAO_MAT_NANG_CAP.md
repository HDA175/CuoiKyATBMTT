# Nâng cấp bảo mật website - Môn An toàn và Bảo mật thông tin

## Hướng dẫn cài đặt

### 1. Không cần cài package
Hệ thống sử dụng **PBKDF2** (có sẵn trong .NET) để hash mật khẩu, tương đương BCrypt về độ an toàn.

---

## Các nâng cấp đã thực hiện

### 1. Bảo mật đăng nhập (Authentication Security) - BCrypt

✔ **Hash mật khẩu bằng PBKDF2** (Rfc2898DeriveBytes - chuẩn .NET)
- Không lưu password dạng text trong bảng TaiKhoan
- Tất cả mật khẩu mới được hash bằng PBKDF2-SHA256 trước khi lưu DB

**Vị trí thay đổi:**
- `Utils/SecurityHelper.cs` - Hàm `HashPassword()`, `VerifyPassword()`, `IsBcryptHash()`
- `UserController.cs` - Login, Register, Google OAuth
- `AccountController.cs` - ChangePassword
- `AdminController.cs` - Login, AddAccount, EditAccount, AddCustomer, AddEmployee, ResetPassword

**Backward compatibility:** Tài khoản cũ có mật khẩu plain text vẫn đăng nhập được. Khi đăng nhập thành công, hệ thống tự động nâng cấp lên BCrypt.

---

### 2. Chống SQL Injection

✔ **Entity Framework** - Đã sử dụng LINQ, query được parameterize tự động

✔ **Input validation/sanitization:**
- `Utils/SecurityHelper.cs` - `SanitizeForSearch()`, `SanitizeForComment()`, `ContainsSqlInjection()`
- Kiểm tra các pattern nguy hiểm: `' OR 1=1 --`, `UNION SELECT`, `DROP TABLE`, v.v.

**Vị trí áp dụng:**
- **Login:** Dùng LINQ `FirstOrDefault` (EF parameterize)
- **Search sản phẩm:** `EvolStoreController.CuaHang` - sanitize `search`
- **Comment/Đánh giá:** `ThemDanhGia`, `SuaDanhGia` - sanitize `noiDung`
- **Filter sản phẩm:** `loai`, `chatLieu`, `mucGia` dùng switch/case (whitelist)
- **Delivery search:** `DeliveryController.Index` - sanitize `searchString`

---

### 3. Bảo mật Upload File

**3.1 Chỉ cho phép định dạng:**
- .jpg, .jpeg, .png, .webp

**3.2 Các biện pháp bổ sung:**
- **Validate MIME type** - Kiểm tra Content-Type
- **Giới hạn kích thước** - Tối đa 5MB
- **Magic bytes validation** - Xác thực nội dung file thực sự là ảnh (chống giả mạo extension)

**Vị trí:**
- `Utils/FileUploadValidator.cs` - `ValidateImageUpload()`
- `AdminController.AddProduct` - `RenameAndSaveImage()`

---

### 4. Mini WAF (Web Application Firewall)

✔ **OWIN Middleware** chặn request có dấu hiệu tấn công
- Kiểm tra Query String và Form
- Pattern: XSS (`<script`, `javascript:`, `onerror=`), SQL Injection (`' or 1=1`, `union select`, `drop table`), v.v.
- Log vào `App_Data/attack_log.txt`
- Trả về 403 + trang "Request Blocked"

**Vị trí:** `Middleware/MiniWafMiddleware.cs` - đăng ký trong `Startup.cs` (trước routing)

**Cách test WAF:** Truy cập `https://localhost:44318/EvolStore/CuaHang?search=' or 1=1` → Trang "Request Blocked". Xem log tại `App_Data/attack_log.txt`

---

### 5. Security Audit Logger - Lịch sử nghi ngờ bị tấn công

✔ **Tự động ghi log** khi phát hiện hoạt động nghi ngờ
- **WAF_BLOCKED** – WAF chặn pattern tấn công
- **SQL_INJECTION** – Phát hiện SQL Injection (search, comment)
- **INVALID_UPLOAD** – Upload file không hợp lệ (sai format, quá lớn, magic bytes giả mạo)

**File log:** `App_Data/SecurityLogs/security_audit.txt`  
**Nội dung:** Timestamp, loại sự kiện, IP, URL, User-Agent, chi tiết

---

## Cấu trúc file mới

```
CuoiKy/
├── Middleware/
│   └── MiniWafMiddleware.cs   # Mini WAF - chặn XSS, SQL Injection
├── Utils/
│   ├── SecurityHelper.cs       # BCrypt + SQL Injection validation
│   ├── SecurityAuditLogger.cs  # Ghi lịch sử nghi ngờ tấn công
│   ├── FileUploadValidator.cs  # Bảo mật upload file
│   └── VnPayLibrary.cs         # (có sẵn)
```

---

## Lưu ý

- Sau khi Restore NuGet, build lại solution
- Cột `MatKhau` trong bảng TaiKhoan sẽ chứa chuỗi BCrypt hash (bắt đầu bằng `$2...`), không còn plain text
- File upload: Nếu file không pass validation (sai format, quá lớn, hoặc không phải ảnh thật), sản phẩm sẽ không được tạo
