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

## Cấu trúc file mới

```
CuoiKy/
├── Utils/
│   ├── SecurityHelper.cs       # BCrypt + SQL Injection validation
│   ├── FileUploadValidator.cs  # Bảo mật upload file
│   └── VnPayLibrary.cs         # (có sẵn)
```

---

## Lưu ý

- Sau khi Restore NuGet, build lại solution
- Cột `MatKhau` trong bảng TaiKhoan sẽ chứa chuỗi BCrypt hash (bắt đầu bằng `$2...`), không còn plain text
- File upload: Nếu file không pass validation (sai format, quá lớn, hoặc không phải ảnh thật), sản phẩm sẽ không được tạo
