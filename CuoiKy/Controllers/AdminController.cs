using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using CuoiKy.Models;
using CuoiKy.Utils;
using ClosedXML.Excel;
using CuoiKy.Filters;
using System.Globalization;


namespace CuoiKy.Controllers
{
    public class AdminController : Controller
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Admin/Login
        public ActionResult Login()
        {
            if (Session["AdminLogged"] != null)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        public ActionResult Login(string username, string password)
        {
            // Xác minh reCAPTCHA
            var captchaResponse = Request["g-recaptcha-response"];
            if (!SecurityHelper.VerifyReCaptcha(captchaResponse, Request.UserHostAddress))
            {
                ViewBag.Error = "Vui lòng xác nhận reCAPTCHA.";
                return View();
            }

            var admin = db.TaiKhoans.FirstOrDefault(t =>
                t.TenDangNhap == username &&
                (t.VaiTro == "Admin" || t.VaiTro == "NhanVien" || t.VaiTro == "NhanVienKho"));

            // Xác minh mật khẩu BCrypt (hoặc backward cho mật khẩu cũ)
            bool passwordValid = false;
            if (admin != null && !string.IsNullOrEmpty(admin.MatKhau))
            {
                if (SecurityHelper.IsBcryptHash(admin.MatKhau))
                    passwordValid = SecurityHelper.VerifyPassword(password, admin.MatKhau);
                else if (admin.MatKhau == password)
                {
                    passwordValid = true;
                    try { admin.MatKhau = SecurityHelper.HashPassword(password); db.SaveChanges(); } catch { }
                }
            }

            if (admin != null && passwordValid)
            {
                Session["AdminLogged"] = true;
                Session["AdminUsername"] = admin.TenDangNhap;
                Session["AdminID"] = admin.MaTK;
                // **QUAN TRỌNG: LƯU VAI TRÒ VÀO SESSION**
                Session["AdminRole"] = admin.VaiTro;
                return RedirectToAction("Dashboard");
            }
            else
            {
                ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng hoặc tài khoản không có quyền truy cập!";
                return View();
            }
        }

        // Kiểm tra đăng nhập
        private bool CheckLogin()
        {
            return Session["AdminLogged"] != null && (bool)Session["AdminLogged"];
        }

        // Dashboard
        [AdminAuthorize("Admin", "NhanVien", "NhanVienKho")]
        public ActionResult Dashboard()
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            // Thống kê
            ViewBag.TongSanPham = db.SanPhams.Count();
            ViewBag.TongDonHang = db.DonHangs.Count();
            ViewBag.TongKhachHang = db.KhachHangs.Count();
            ViewBag.TongNhanVien = db.NhanViens.Count();

            // Doanh thu tháng
            var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var completedOrders = db.DonHangs
                .Where(d => d.NgayDat >= firstDayOfMonth && d.NgayDat <= lastDayOfMonth && d.TrangThai == "Giao hàng thành công")
                .ToList();

            decimal monthlyRevenue = 0;
            foreach (var order in completedOrders)
            {
                var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == order.MaDH).ToList();
                monthlyRevenue += orderDetails.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0);
            }
            ViewBag.DoanhThuThang = monthlyRevenue;

            // Đơn hàng mới
            ViewBag.DonHangMoi = db.DonHangs
                .Where(d => d.TrangThai == "Chờ xử lý")
                .OrderByDescending(d => d.NgayDat)
                .Take(5)
                .ToList();

            return View();
        }


        // Quản lý Giảm giá Sản phẩm
        [AdminAuthorize("Admin", "NhanVien", "NhanVienKho")]
        public ActionResult Sales()
        {
            var products = db.SanPhams
                .Include(p => p.GiamGias)
                .OrderBy(p => p.MaSP)
                .ToList();

            var now = DateTime.Now;

            // Tính giá gốc cho từng sản phẩm
            foreach (var product in products)
            {
                var giamGiaHienTai = product.GiamGias
                    .Where(g => g.NgayBatDau <= now && (g.NgayKetThuc == null || g.NgayKetThuc >= now))
                    .OrderByDescending(g => g.PhanTramGiam)
                    .FirstOrDefault();

                if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                {
                    // Tính ngược lại giá gốc từ giá hiện tại và phần trăm giảm
                    product.GiaGoc = product.Gia / (1 - giamGiaHienTai.PhanTramGiam);
                }
                else
                {
                    // Nếu không có giảm giá, giá gốc = giá hiện tại
                    product.GiaGoc = product.Gia;
                }
            }

            return View(products);
        }

        // POST: Áp dụng giảm giá cho sản phẩm
        [AdminAuthorize("Admin", "NhanVien")]
        [HttpPost]
        public ActionResult ApplyDiscount(int maSP, decimal discountPercentage, string endDate = null, bool updatePriceInProduct = true)
        {
            try
            {
                var product = db.SanPhams
                    .Include(p => p.GiamGias)
                    .FirstOrDefault(p => p.MaSP == maSP);

                if (product == null)
                {
                    return Json(new { success = false, message = "Sản phẩm không tồn tại." });
                }

                if (discountPercentage < 0 || discountPercentage > 100)
                {
                    return Json(new { success = false, message = "Phần trăm giảm giá phải từ 0 đến 100." });
                }

                decimal rate = discountPercentage / 100m;
                var todayDate = DateTime.Now.Date;

                // Chuyển đổi ngày kết thúc từ string
                DateTime? endDateValue = null;
                if (!string.IsNullOrEmpty(endDate))
                {
                    if (DateTime.TryParseExact(endDate, "dd/MM/yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                    {
                        endDateValue = parsedDate.Date;
                        if (endDateValue < todayDate)
                        {
                            return Json(new { success = false, message = "Ngày kết thúc không được nhỏ hơn ngày hiện tại." });
                        }
                    }
                    else
                    {
                        return Json(new { success = false, message = "Định dạng ngày không hợp lệ." });
                    }
                }

                // Tìm giảm giá hiện tại để tính giá gốc
                var giamGiaHienTai = product.GiamGias
                    .Where(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate))
                    .FirstOrDefault();

                decimal giaGoc = product.Gia;
                if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                {
                    // Tính ngược lại giá gốc
                    giaGoc = product.Gia / (1 - giamGiaHienTai.PhanTramGiam);
                }

                // Hủy giảm giá cũ đang có hiệu lực
                var activeDiscounts = product.GiamGias
                    .Where(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate))
                    .ToList();

                foreach (var discount in activeDiscounts)
                {
                    discount.NgayKetThuc = todayDate.AddDays(-1);
                    db.Entry(discount).State = EntityState.Modified;
                }

                // Nếu giảm giá là 0%, chỉ hủy giảm giá cũ
                if (rate == 0)
                {
                    // Cập nhật giá sản phẩm về giá gốc nếu được yêu cầu
                    if (updatePriceInProduct)
                    {
                        product.Gia = giaGoc;
                        db.Entry(product).State = EntityState.Modified;
                    }

                    db.SaveChanges();
                    return Json(new { success = true, message = $"Đã loại bỏ giảm giá cho sản phẩm {product.TenSP}." });
                }

                // Thêm giảm giá mới
                var newDiscount = new GiamGia
                {
                    MaSP = maSP,
                    PhanTramGiam = rate,
                    NgayBatDau = todayDate,
                    NgayKetThuc = endDateValue
                };

                db.GiamGias.Add(newDiscount);

                // Cập nhật giá sản phẩm nếu được yêu cầu
                if (updatePriceInProduct)
                {
                    decimal newPrice = giaGoc * (1 - rate);
                    product.Gia = newPrice;
                    db.Entry(product).State = EntityState.Modified;
                }

                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = $"Đã áp dụng giảm giá {discountPercentage}% cho sản phẩm {product.TenSP}."
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ApplyDiscount: {ex.Message}\n{ex.StackTrace}");
                return Json(new { success = false, message = "Lỗi hệ thống khi cập nhật giảm giá." });
            }
        }

        // POST: Xóa giảm giá
        [AdminAuthorize("Admin", "NhanVien")]
        [HttpPost]
        public ActionResult RemoveDiscount(int maSP, bool updatePriceInProduct = true)
        {
            try
            {
                var product = db.SanPhams
                    .Include(p => p.GiamGias)
                    .FirstOrDefault(p => p.MaSP == maSP);

                if (product == null)
                {
                    return Json(new { success = false, message = "Sản phẩm không tồn tại." });
                }

                var todayDate = DateTime.Now.Date;

                // Tìm giảm giá hiện tại để tính giá gốc
                var giamGiaHienTai = product.GiamGias
                    .Where(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate))
                    .FirstOrDefault();

                decimal giaGoc = product.Gia;
                if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                {
                    // Tính ngược lại giá gốc
                    giaGoc = product.Gia / (1 - giamGiaHienTai.PhanTramGiam);
                }

                // Tìm tất cả giảm giá còn hiệu lực
                var activeDiscounts = product.GiamGias
                    .Where(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate))
                    .ToList();

                // Đặt ngày kết thúc là hôm qua
                foreach (var discount in activeDiscounts)
                {
                    discount.NgayKetThuc = todayDate.AddDays(-1);
                    db.Entry(discount).State = EntityState.Modified;
                }

                // Cập nhật giá sản phẩm về giá gốc nếu được yêu cầu
                if (updatePriceInProduct)
                {
                    product.Gia = giaGoc;
                    db.Entry(product).State = EntityState.Modified;
                }

                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = $"Đã xóa giảm giá cho sản phẩm {product.TenSP}."
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RemoveDiscount: {ex.Message}\n{ex.StackTrace}");
                return Json(new { success = false, message = "Lỗi hệ thống khi xóa giảm giá." });
            }
        }

        // POST: Áp dụng giảm giá hàng loạt
        [AdminAuthorize("Admin", "NhanVien")]
        [HttpPost]
        public ActionResult ApplyBulkDiscount(decimal discountPercentage, string endDate = null,
            bool overwriteExisting = false, bool updatePriceInProduct = true)
        {
            try
            {
                if (discountPercentage < 0 || discountPercentage > 100)
                {
                    return Json(new { success = false, message = "Phần trăm giảm giá phải từ 0 đến 100." });
                }

                decimal rate = discountPercentage / 100m;
                var todayDate = DateTime.Now.Date;

                // Chuyển đổi ngày kết thúc
                DateTime? endDateValue = null;
                if (!string.IsNullOrEmpty(endDate))
                {
                    if (DateTime.TryParseExact(endDate, "dd/MM/yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                    {
                        endDateValue = parsedDate.Date;
                    }
                }

                // Lấy tất cả sản phẩm với giảm giá
                var allProducts = db.SanPhams
                    .Include(p => p.GiamGias)
                    .ToList();

                int updatedCount = 0;

                foreach (var product in allProducts)
                {
                    // Tính giá gốc
                    decimal giaGoc = product.Gia;
                    var giamGiaHienTai = product.GiamGias
                        .Where(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate))
                        .FirstOrDefault();

                    if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                    {
                        giaGoc = product.Gia / (1 - giamGiaHienTai.PhanTramGiam);
                    }

                    // Nếu không ghi đè, kiểm tra xem sản phẩm đã có giảm giá chưa
                    if (!overwriteExisting && giamGiaHienTai != null)
                    {
                        continue;
                    }

                    // Hủy giảm giá cũ nếu ghi đè
                    if (overwriteExisting && giamGiaHienTai != null)
                    {
                        giamGiaHienTai.NgayKetThuc = todayDate.AddDays(-1);
                        db.Entry(giamGiaHienTai).State = EntityState.Modified;
                    }

                    // Thêm giảm giá mới
                    var newDiscount = new GiamGia
                    {
                        MaSP = product.MaSP,
                        PhanTramGiam = rate,
                        NgayBatDau = todayDate,
                        NgayKetThuc = endDateValue
                    };

                    db.GiamGias.Add(newDiscount);

                    // Cập nhật giá sản phẩm
                    if (updatePriceInProduct)
                    {
                        decimal newPrice = giaGoc * (1 - rate);
                        product.Gia = newPrice;
                        db.Entry(product).State = EntityState.Modified;
                    }

                    updatedCount++;
                }

                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = $"Đã áp dụng giảm giá {discountPercentage}% cho {updatedCount} sản phẩm."
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ApplyBulkDiscount: {ex.Message}\n{ex.StackTrace}");
                return Json(new { success = false, message = "Lỗi hệ thống khi áp dụng giảm giá hàng loạt." });
            }
        }

        // POST: Cập nhật giá sản phẩm hàng loạt
        [AdminAuthorize("Admin", "NhanVien")]
        [HttpPost]
        public ActionResult UpdateProductPrices(string method, decimal value, bool applyToDiscounted = false)
        {
            try
            {
                var products = db.SanPhams
                    .Include(p => p.GiamGias)
                    .ToList();

                int updatedCount = 0;
                var todayDate = DateTime.Now.Date;

                foreach (var product in products)
                {
                    // Kiểm tra nếu sản phẩm đang giảm giá
                    var hasActiveDiscount = product.GiamGias
                        .Any(g => g.NgayBatDau <= todayDate && (g.NgayKetThuc == null || g.NgayKetThuc >= todayDate));

                    if (hasActiveDiscount && !applyToDiscounted)
                    {
                        continue; // Bỏ qua sản phẩm đang giảm giá
                    }

                    decimal currentPrice = product.Gia;
                    decimal newPrice = currentPrice;

                    switch (method)
                    {
                        case "percent": // Tăng/giảm theo %
                            newPrice = currentPrice * (1 + value / 100m);
                            break;
                        case "fixed": // Tăng/giảm cố định
                            newPrice = currentPrice + value;
                            break;
                        case "newprice": // Đặt giá mới
                            newPrice = value;
                            break;
                    }

                    // Đảm bảo giá không âm
                    if (newPrice < 0) newPrice = 0;

                    product.Gia = newPrice;
                    db.Entry(product).State = EntityState.Modified;
                    updatedCount++;
                }

                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = $"Đã cập nhật giá cho {updatedCount} sản phẩm."
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UpdateProductPrices: {ex.Message}\n{ex.StackTrace}");
                return Json(new { success = false, message = "Lỗi hệ thống khi cập nhật giá." });
            }
        }


        public ActionResult ExportSuppliersToExcel()
        {
            // Kiểm tra vai trò nếu cần thiết
            if (Session["AdminRole"] as string != "Admin" && Session["AdminRole"] as string != "NhanVienKho")
            {
                return RedirectToAction("Unauthorized");
            }

            using (var db = new EvolStoreEntities2())
            {
                var suppliers = db.NhaCungCaps.ToList();

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("NhaCungCap");

                    // Tựa đề báo cáo
                    worksheet.Cell("A1").Value = "DANH SÁCH NHÀ CUNG CẤP";
                    worksheet.Range("A1:E1").Merge();
                    worksheet.Cell("A1").Style.Font.Bold = true;
                    worksheet.Cell("A1").Style.Font.FontSize = 16;
                    worksheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Header Cột (Bắt đầu từ hàng 3)
                    worksheet.Cell("A3").Value = "Mã NCC";
                    worksheet.Cell("B3").Value = "Tên NCC";
                    worksheet.Cell("C3").Value = "Địa Chỉ";
                    worksheet.Cell("D3").Value = "Điện Thoại";
                    worksheet.Cell("E3").Value = "Email";

                    // Định dạng Header
                    worksheet.Range("A3:E3").Style.Font.Bold = true;
                    worksheet.Range("A3:E3").Style.Fill.BackgroundColor = XLColor.LightGray;

                    // Dữ liệu
                    int row = 4;
                    foreach (var ncc in suppliers)
                    {
                        worksheet.Cell(row, 1).Value = ncc.MaNCC;
                        worksheet.Cell(row, 2).Value = ncc.TenNCC;
                        worksheet.Cell(row, 3).Value = ncc.DiaChi;
                        worksheet.Cell(row, 4).Value = ncc.SoDienThoai;
                        worksheet.Cell(row, 5).Value = ncc.Email;
                        row++;
                    }

                    worksheet.Columns().AdjustToContents();

                    // Trả về file
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        var fileName = $"NhaCungCap_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.xlsx";

                        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                    }
                }
            }
        }


        // Trong AdminController.cs
        [HttpPost]
        [ValidateAntiForgeryToken] // Nên có để tăng bảo mật
        public ActionResult SaveNewSupplier(CuoiKy.Models.NhaCungCap newSupplier)
        {
            // 1. Kiểm tra vai trò
            if (Session["AdminRole"] as string != "Admin" && Session["AdminRole"] as string != "NhanVienKho")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền thực hiện chức năng này.";
                return RedirectToAction("WarehouseManager");
            }

            // 2. Kiểm tra tính hợp lệ của Model
            if (ModelState.IsValid)
            {
                using (var db = new EvolStoreEntities2())
                {
                    try
                    {
                        // Thêm Nhà Cung Cấp mới vào database
                        db.NhaCungCaps.Add(newSupplier);
                        db.SaveChanges();

                        TempData["SuccessMessage"] = $"Đã thêm Nhà Cung Cấp '{newSupplier.TenNCC}' thành công!";

                        // Chuyển hướng về trang WarehouseManager để làm mới danh sách
                        return RedirectToAction("WarehouseManager");
                    }
                    catch (Exception ex)
                    {
                        // Xử lý lỗi database (ví dụ: trùng khóa chính)
                        TempData["ErrorMessage"] = "Lỗi khi lưu Nhà Cung Cấp: " + ex.Message;
                        // Nếu muốn giữ lại dữ liệu đã nhập, bạn có thể truyền lại Model vào View
                    }
                }
            }
            else
            {
                // Model không hợp lệ (ví dụ: thiếu trường required)
                TempData["ErrorMessage"] = "Vui lòng điền đầy đủ thông tin bắt buộc.";
            }

            // Nếu có lỗi, chuyển hướng về trang quản lý kho
            // Lưu ý: Sau khi Redirect, tab mặc định sẽ là Tồn Kho (TonKho). 
            // Nếu muốn chuyển hướng về tab NCC, bạn cần truyền thêm tham số (ví dụ: return RedirectToAction("WarehouseManager", new { tab = "NhaCungCap" })) 
            // và xử lý tham số đó trong JavaScript của View.
            return RedirectToAction("WarehouseManager");
        }


        public ActionResult AddSupplier()
        {
            // Kiểm tra vai trò (Chỉ Admin hoặc NV Kho được phép tạo NCC)
            if (Session["AdminRole"] as string != "Admin" && Session["AdminRole"] as string != "NhanVienKho")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền truy cập chức năng này.";
                return RedirectToAction("WarehouseManager");
            }

            // Trả về view để thêm NCC (Ví dụ: Views/Admin/AddSupplier.cshtml)
            return View();
        }


        public ActionResult WarehouseManager()
        {
            // Kiểm tra vai trò
            if (Session["AdminRole"] as string != "Admin" && Session["AdminRole"] as string != "NhanVienKho")
            {
                return RedirectToAction("Unauthorized"); // Hoặc trang lỗi
            }

            using (var db = new EvolStoreEntities2()) // Thay thế bằng DbContext của bạn
            {
                // 1. Lấy danh sách Sản phẩm (để kiểm tra tồn kho)
                var products = db.SanPhams
                                 .Include("NhaCungCap") // Đảm bảo include để hiển thị tên NCC
                                 .OrderBy(p => p.MaSP)
                                 .ToList();

                // 2. Lấy danh sách Nhà Cung Cấp (bảng bạn đã cung cấp)
                var suppliers = db.NhaCungCaps.ToList();

                // Truyền Nhà Cung Cấp qua ViewBag
                ViewBag.NhaCungCaps = suppliers;

                // Truyền Sản phẩm qua Model
                return View(products);
            }
        }


        // Trong AdminController.cs

        [HttpPost]
        [ValidateAntiForgeryToken] // Nên có
        public ActionResult DeleteSupplier(int id)
        {
            // 1. Kiểm tra vai trò
            if (Session["AdminRole"] as string != "Admin" && Session["AdminRole"] as string != "NhanVienKho")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền thực hiện chức năng xóa.";
                return RedirectToAction("WarehouseManager");
            }

            using (var db = new EvolStoreEntities2())
            {
                var supplierToDelete = db.NhaCungCaps.Find(id);

                if (supplierToDelete == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy Nhà Cung Cấp này.";
                }
                else
                {
                    try
                    {
                        // TODO: Quan trọng!
                        // Bạn cần kiểm tra xem NCC này có liên quan đến Sản phẩm (SanPham) hay Phiếu nhập kho (PhieuNhapKho) nào không. 
                        // Nếu có, bạn cần xử lý (ví dụ: gán null, xóa liên quan, hoặc chặn xóa).

                        // Giả sử không có ràng buộc hoặc ràng buộc đã được xử lý Cascade
                        db.NhaCungCaps.Remove(supplierToDelete);
                        db.SaveChanges();

                        TempData["SuccessMessage"] = $"Đã xóa Nhà Cung Cấp '{supplierToDelete.TenNCC}' thành công.";
                    }
                    catch (Exception ex)
                    {
                        TempData["ErrorMessage"] = "Không thể xóa Nhà Cung Cấp này do có dữ liệu liên quan trong hệ thống: " + ex.Message;
                    }
                }
            }

            // Chuyển hướng về trang quản lý kho (WarehouseManager)
            return RedirectToAction("WarehouseManager");
        }


        public ActionResult ExportInventoryToExcel()
        {
            using (var db = new EvolStoreEntities2())
            {
                var products = db.SanPhams.Include("NhaCungCap").ToList();

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("BaoCaoTonKho");

                    // Tựa đề báo cáo
                    worksheet.Cell("A1").Value = "BÁO CÁO SẢN PHẨM TỒN KHO";
                    worksheet.Range("A1:E1").Merge();
                    worksheet.Cell("A1").Style.Font.Bold = true;
                    worksheet.Cell("A1").Style.Font.FontSize = 16;
                    worksheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Header Cột (Bắt đầu từ hàng 3)
                    worksheet.Cell("A3").Value = "Mã SP";
                    worksheet.Cell("B3").Value = "Tên Sản phẩm";
                    worksheet.Cell("C3").Value = "Tồn kho";
                    worksheet.Cell("D3").Value = "Giá bán";
                    worksheet.Cell("E3").Value = "Nhà cung cấp";

                    // Định dạng Header
                    worksheet.Range("A3:E3").Style.Font.Bold = true;
                    worksheet.Range("A3:E3").Style.Fill.BackgroundColor = XLColor.LightGray;

                    // Dữ liệu
                    int row = 4;
                    foreach (var product in products)
                    {
                        worksheet.Cell(row, 1).Value = product.MaSP;
                        worksheet.Cell(row, 2).Value = product.TenSP;
                        worksheet.Cell(row, 3).Value = product.SoLuongTon;
                        worksheet.Cell(row, 4).Value = product.Gia;
                        worksheet.Cell(row, 5).Value = product.NhaCungCap?.TenNCC ?? "N/A";

                        // Định dạng cột Giá bán là tiền tệ
                        worksheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0 ₫";

                        row++;
                    }

                    // AutoFit Columns
                    worksheet.Columns().AdjustToContents();

                    // Trả về file
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        var fileName = $"BaoCaoTonKho_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.xlsx";

                        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                    }
                }
            }
        }


        // ===========================
        // QUẢN LÝ SẢN PHẨM - CRUD HOÀN CHỈNH
        // ===========================
        [AdminAuthorize("Admin", "NhanVien", "NhanVienKho")]
        public ActionResult Products()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var products = db.SanPhams.ToList();
            ViewBag.NhaCungCapList = db.NhaCungCaps.ToList();
            return View(db.SanPhams.Include(s => s.NhaCungCap).ToList());
        }

        // Thêm sản phẩm
        [AdminAuthorize("Admin", "NhanVien", "NhanVienKho")]
        public ActionResult AddProduct()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            ViewBag.NhaCungCap = db.NhaCungCaps.ToList(); // Sửa thành NhaCungCap (giống trong View)
            return View();
        }

        // POST: Thêm sản phẩm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddProduct(SanPham product, HttpPostedFileBase hinhAnh1, HttpPostedFileBase hinhAnh2, HttpPostedFileBase hinhAnh3, HttpPostedFileBase hinhAnh4)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                if (ModelState.IsValid)
                {
                    // THÊM SẢN PHẨM TRƯỚC để có MaSP
                    db.SanPhams.Add(product);
                    db.SaveChanges();

                    // XỬ LÝ UPLOAD ẢNH - Bảo mật: extension, MIME, size, magic bytes
                    var uploadResult = RenameAndSaveImage(hinhAnh1, product.MaSP, "a");
                    if (!string.IsNullOrEmpty(uploadResult))
                    {
                        db.SanPhams.Remove(product);
                        db.SaveChanges();
                        ViewBag.UploadError = uploadResult;
                        ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                        return View(product);
                    }
                    uploadResult = RenameAndSaveImage(hinhAnh2, product.MaSP, "b");
                    if (!string.IsNullOrEmpty(uploadResult))
                    {
                        db.SanPhams.Remove(product);
                        db.SaveChanges();
                        ViewBag.UploadError = uploadResult;
                        ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                        return View(product);
                    }
                    uploadResult = RenameAndSaveImage(hinhAnh3, product.MaSP, "c");
                    if (!string.IsNullOrEmpty(uploadResult))
                    {
                        db.SanPhams.Remove(product);
                        db.SaveChanges();
                        ViewBag.UploadError = uploadResult;
                        ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                        return View(product);
                    }
                    uploadResult = RenameAndSaveImage(hinhAnh4, product.MaSP, "d");
                    if (!string.IsNullOrEmpty(uploadResult))
                    {
                        db.SanPhams.Remove(product);
                        db.SaveChanges();
                        ViewBag.UploadError = uploadResult;
                        ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                        return View(product);
                    }

                    TempData["SuccessMessage"] = $"Thêm sản phẩm '{product.TenSP}' thành công!";
                    return RedirectToAction("Products");
                }

                ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                return View(product);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi thêm sản phẩm: " + ex.Message;
                ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                return View(product);
            }
        }

        /// <summary>
        /// Bảo mật upload file: chỉ .jpg/.jpeg/.png/.webp, validate MIME, size 5MB, magic bytes.
        /// Trả về null nếu OK, trả về chuỗi lỗi nếu có lỗi.
        /// </summary>
        private string RenameAndSaveImage(HttpPostedFileBase file, int maSP, string suffix)
        {
            if (file == null || file.ContentLength == 0) return null;

            var validation = FileUploadValidator.ValidateImageUpload(file);
            if (!validation.IsValid)
                return validation.ErrorMessage;

            try
            {
                // Lưu với .jpg để tương thích views hiện tại (đã validate là ảnh hợp lệ)
                var newFileName = $"{maSP}{suffix}.jpg";
                var path = Path.Combine(Server.MapPath("~/Images/"), Path.GetFileName(newFileName));

                var directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                file.SaveAs(path);
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi upload ảnh: " + ex.Message);
                return "Lỗi lưu file: " + ex.Message;
            }
        }


        // GET: Sửa sản phẩm
        public ActionResult EditProduct(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var product = db.SanPhams.Find(id);
            if (product == null) return HttpNotFound();

            // ĐẢM BẢO KHÔNG NULL
            var nhaCungCapList = db.NhaCungCaps.ToList();
            if (nhaCungCapList == null || !nhaCungCapList.Any())
            {
                TempData["ErrorMessage"] = "Không có nhà cung cấp nào. Vui lòng thêm nhà cung cấp trước khi sửa sản phẩm.";
                return RedirectToAction("Products");
            }

            ViewBag.NhaCungCap = nhaCungCapList; // Sửa thành NhaCungCap
            return View(product);
        }

        // POST: Sửa sản phẩm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditProduct(SanPham product)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                if (ModelState.IsValid)
                {
                    // Lấy sản phẩm hiện tại từ database
                    var existingProduct = db.SanPhams.Find(product.MaSP);
                    if (existingProduct != null)
                    {
                        // Cập nhật các trường thông tin
                        existingProduct.TenSP = product.TenSP;
                        existingProduct.Gia = product.Gia;
                        existingProduct.Loai = product.Loai;
                        existingProduct.ChatLieu = product.ChatLieu;
                        existingProduct.MauSac = product.MauSac;
                        existingProduct.KichThuoc = product.KichThuoc;
                        existingProduct.MoTa = product.MoTa;
                        existingProduct.SoLuongTon = product.SoLuongTon;
                        existingProduct.MaNCC = product.MaNCC;

                        db.SaveChanges();
                        TempData["SuccessMessage"] = "Cập nhật sản phẩm thành công!";
                        return RedirectToAction("Products");
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Không tìm thấy sản phẩm để cập nhật!";
                        return RedirectToAction("Products");
                    }
                }

                // Nếu ModelState không valid
                ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                return View(product);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                ViewBag.NhaCungCap = db.NhaCungCaps.ToList();
                return View(product);
            }
        }

        public ActionResult DeleteProduct(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                var product = db.SanPhams.Find(id);
                if (product == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy sản phẩm để xóa!";
                    return RedirectToAction("Products");
                }

                // Kiểm tra xem sản phẩm có trong đơn hàng không
                var hasOrderDetails = db.ChiTietDonHangs.Any(ct => ct.MaSP == id);
                if (hasOrderDetails)
                {
                    TempData["ErrorMessage"] = $"Không thể xóa sản phẩm '{product.TenSP}' vì có trong đơn hàng!";
                    return RedirectToAction("Products");
                }

                db.SanPhams.Remove(product);
                db.SaveChanges();

                TempData["SuccessMessage"] = $"Đã xóa sản phẩm '{product.TenSP}' thành công!";
                return RedirectToAction("Products");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi xóa sản phẩm: " + ex.Message;
                return RedirectToAction("Products");
            }
        }






        // ===========================
        // EXPORT DOANH THU SANG EXCEL
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportRevenue(DateTime fromDate, DateTime toDate)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // Validate dates
                if (fromDate > toDate)
                {
                    TempData["ErrorMessage"] = "Ngày bắt đầu không thể lớn hơn ngày kết thúc!";
                    return RedirectToAction("RevenueByDate");
                }

                // Lấy dữ liệu doanh thu
                var revenueData = GetRevenueData(fromDate, toDate.AddDays(1));

                if (!revenueData.Any())
                {
                    TempData["ErrorMessage"] = "Không có dữ liệu để xuất Excel!";
                    return RedirectToAction("RevenueByDate");
                }

                // Tạo workbook với ClosedXML
                using (var workbook = new ClosedXML.Excel.XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("DoanhThu");

                    // Tiêu đề
                    worksheet.Cell("A1").Value = "BÁO CÁO DOANH THU THEO NGÀY";
                    worksheet.Range("A1:F1").Merge();
                    worksheet.Cell("A1").Style.Font.Bold = true;
                    worksheet.Cell("A1").Style.Font.FontSize = 16;
                    worksheet.Cell("A1").Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                    // Thông tin khoảng thời gian
                    worksheet.Cell("A2").Value = $"Từ ngày: {fromDate:dd/MM/yyyy} đến ngày: {toDate:dd/MM/yyyy}";
                    worksheet.Range("A2:F2").Merge();
                    worksheet.Cell("A2").Style.Font.Bold = true;

                    // Ngày xuất báo cáo
                    worksheet.Cell("A3").Value = $"Ngày xuất báo cáo: {DateTime.Now:dd/MM/yyyy HH:mm}";
                    worksheet.Range("A3:F3").Merge();
                    worksheet.Cell("A3").Style.Font.Italic = true;

                    // Headers
                    var headers = new string[] { "Ngày", "Thứ", "Doanh thu", "Số đơn hàng", "Số khách hàng", "Giá trị đơn trung bình" };

                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = worksheet.Cell(5, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                        cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                    }

                    // Dữ liệu
                    int row = 6;
                    foreach (var item in revenueData)
                    {
                        worksheet.Cell(row, 1).Value = item.Date.ToString("dd/MM/yyyy");
                        worksheet.Cell(row, 2).Value = item.DayOfWeekDisplay;
                        worksheet.Cell(row, 3).Value = item.TotalRevenue;
                        worksheet.Cell(row, 4).Value = item.OrderCount;
                        worksheet.Cell(row, 5).Value = item.CustomerCount;
                        worksheet.Cell(row, 6).Value = item.AverageOrderValue;

                        // Định dạng số
                        worksheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                        worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                        // Highlight cuối tuần
                        if (item.Date.DayOfWeek == DayOfWeek.Saturday || item.Date.DayOfWeek == DayOfWeek.Sunday)
                        {
                            for (int col = 1; col <= 6; col++)
                            {
                                worksheet.Cell(row, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightYellow;
                            }
                        }

                        row++;
                    }

                    // Tổng cộng
                    worksheet.Cell(row, 1).Value = "TỔNG CỘNG";
                    worksheet.Cell(row, 1).Style.Font.Bold = true;
                    worksheet.Cell(row, 3).FormulaA1 = $"SUM(C6:C{row - 1})";
                    worksheet.Cell(row, 4).FormulaA1 = $"SUM(D6:D{row - 1})";
                    worksheet.Cell(row, 5).FormulaA1 = $"SUM(E6:E{row - 1})";

                    // Định dạng tổng cộng
                    worksheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                    for (int col = 1; col <= 6; col++)
                    {
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGreen;
                        worksheet.Cell(row, col).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                    }

                    // Auto-fit columns
                    worksheet.Columns().AdjustToContents();

                    // Tên file
                    string fileName = $"DoanhThu_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.xlsx";

                    // Trả về file
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return File(stream.ToArray(),
                                   "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                   fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xuất Excel: {ex.Message}";
                return RedirectToAction("RevenueByDate");
            }
        }


        // ===========================
        // THỐNG KÊ DOANH THU THEO NGÀY
        // ===========================
        [AdminAuthorize("Admin", "NhanVien")]
        public ActionResult RevenueByDate()
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // Mặc định: thống kê 30 ngày gần nhất
                var endDate = DateTime.Now;
                var startDate = endDate.AddDays(-30);

                ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
                ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");

                var revenueData = GetRevenueData(startDate, endDate);
                return View(revenueData);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Lỗi khi tải thống kê doanh thu: " + ex.Message;
                return View(new List<RevenueDailyViewModel>());
            }
        }

        [HttpPost]
        public ActionResult RevenueByDate(DateTime fromDate, DateTime toDate)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // Validate dates
                if (fromDate > toDate)
                {
                    TempData["ErrorMessage"] = "Ngày bắt đầu không thể lớn hơn ngày kết thúc!";
                    return RedirectToAction("RevenueByDate");
                }

                // Giới hạn tối đa 365 ngày
                if ((toDate - fromDate).Days > 365)
                {
                    TempData["ErrorMessage"] = "Chỉ có thể thống kê tối đa 365 ngày!";
                    return RedirectToAction("RevenueByDate");
                }

                ViewBag.StartDate = fromDate.ToString("yyyy-MM-dd");
                ViewBag.EndDate = toDate.ToString("yyyy-MM-dd");

                var revenueData = GetRevenueData(fromDate, toDate.AddDays(1)); // Thêm 1 ngày để bao gồm toDate
                return View(revenueData);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Lỗi khi tải thống kê doanh thu: " + ex.Message;
                return View(new List<RevenueDailyViewModel>());
            }
        }

        // Hàm lấy dữ liệu doanh thu
        private List<RevenueDailyViewModel> GetRevenueData(DateTime fromDate, DateTime toDate)
        {
            var revenueData = new List<RevenueDailyViewModel>();

            // Lấy tất cả đơn hàng hoàn thành trong khoảng thời gian
            var completedOrders = db.DonHangs
                .Where(d => d.NgayDat >= fromDate &&
                           d.NgayDat < toDate &&
                           d.TrangThai == "Giao hàng thành công")
                .ToList();

            // Nhóm theo ngày
            var dailyGroups = completedOrders
                .GroupBy(d => d.NgayDat.Value.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalRevenue = g.Sum(order =>
                    {
                        var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == order.MaDH).ToList();
                        return orderDetails.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0) + (order.PhiGiaoHang ?? 0);
                    }),
                    OrderCount = g.Count(),
                    CustomerCount = g.Select(o => o.MaKH).Distinct().Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            // Điền dữ liệu cho tất cả các ngày trong khoảng (kể cả ngày không có doanh thu)
            for (var date = fromDate.Date; date < toDate.Date; date = date.AddDays(1))
            {
                var dayData = dailyGroups.FirstOrDefault(g => g.Date == date);

                revenueData.Add(new RevenueDailyViewModel
                {
                    Date = date,
                    TotalRevenue = dayData?.TotalRevenue ?? 0,
                    OrderCount = dayData?.OrderCount ?? 0,
                    CustomerCount = dayData?.CustomerCount ?? 0,
                    AverageOrderValue = dayData?.OrderCount > 0 ? dayData.TotalRevenue / dayData.OrderCount : 0
                });
            }

            return revenueData;
        }


        // ===========================
        // QUẢN LÝ ĐƠN HÀNG - CRUD HOÀN CHỈNH
        // ===========================
        public ActionResult Orders()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var orders = db.DonHangs
                .Include(d => d.KhachHang)
                .Include(d => d.ChiTietDonHangs)
                .OrderByDescending(d => d.NgayDat)
                .ToList();
            return View(orders);
        }

        // Chi tiết đơn hàng
        public ActionResult OrderDetails(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var order = db.DonHangs.Find(id);
            if (order == null) return HttpNotFound();

            var customer = db.KhachHangs.Find(order.MaKH);
            ViewBag.Customer = customer;

            // Lấy thông tin người nhận
            var recipient = db.NguoiNhans.FirstOrDefault(n => n.MaDH == id);
            ViewBag.Recipient = recipient;

            var orderDetails = db.ChiTietDonHangs
                .Where(ct => ct.MaDH == id)
                .Include(ct => ct.SanPham)
                .ToList();
            ViewBag.OrderDetails = orderDetails;

            return View(order);
        }

        // Cập nhật trạng thái đơn hàng với 7 trạng thái mới
        [HttpPost]
        public ActionResult UpdateOrderStatus(int orderId, string status)
        {
            if (!CheckLogin()) return Json(new { success = false });

            var order = db.DonHangs.Find(orderId);
            if (order != null)
            {
                order.TrangThai = status;
                db.SaveChanges();
                return Json(new { success = true, message = "Cập nhật trạng thái thành công!" });
            }

            return Json(new { success = false, message = "Không tìm thấy đơn hàng!" });
        }

        // Xóa đơn hàng
        public ActionResult DeleteOrder(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var order = db.DonHangs.Find(id);
            if (order == null) return HttpNotFound();

            // Xóa chi tiết đơn hàng trước
            var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == id).ToList();
            foreach (var detail in orderDetails)
            {
                db.ChiTietDonHangs.Remove(detail);
            }

            // Xóa đơn hàng
            db.DonHangs.Remove(order);
            db.SaveChanges();

            TempData["SuccessMessage"] = "Xóa đơn hàng thành công!";
            return RedirectToAction("Orders");
        }

        // ===========================
        // QUẢN LÝ KHÁCH HÀNG - CRUD HOÀN CHỈNH
        // ===========================
        [AdminAuthorize("Admin", "NhanVien")]
        public ActionResult Customers()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var customers = db.KhachHangs.ToList();
            return View(db.KhachHangs.ToList());
        }

        // Thêm khách hàng - GET
        public ActionResult AddCustomer()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            return View();
        }

        // Thêm khách hàng - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddCustomer(KhachHang customer)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                if (ModelState.IsValid)
                {
                    // Kiểm tra email đã tồn tại chưa
                    var existingCustomer = db.KhachHangs.FirstOrDefault(k => k.Email == customer.Email);
                    if (existingCustomer != null)
                    {
                        ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống!");
                        return View(customer);
                    }

                    db.KhachHangs.Add(customer);
                    db.SaveChanges();

                    // Tạo tài khoản mặc định - BCrypt hash
                    var taiKhoan = new TaiKhoan
                    {
                        TenDangNhap = "customer" + customer.MaKH,
                        MatKhau = SecurityHelper.HashPassword("123456"),
                        VaiTro = "KhachHang",
                        TrangThai = true,
                        MaKH = customer.MaKH,
                        NgayTao = DateTime.Now
                    };
                    db.TaiKhoans.Add(taiKhoan);
                    db.SaveChanges();

                    TempData["SuccessMessage"] = $"Thêm khách hàng '{customer.HoTen}' thành công! Tài khoản: customer{customer.MaKH} | Mật khẩu: 123456";
                    return RedirectToAction("Customers");
                }

                return View(customer);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi thêm khách hàng: " + ex.Message;
                return View(customer);
            }
        }

        // Sửa khách hàng - GET
        public ActionResult EditCustomer(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var customer = db.KhachHangs.Find(id);
            if (customer == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy khách hàng!";
                return RedirectToAction("Customers");
            }

            return View(customer);
        }

        // Sửa khách hàng - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditCustomer(KhachHang customer)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                if (ModelState.IsValid)
                {
                    // Kiểm tra email trùng (trừ chính khách hàng đang sửa)
                    var existingCustomer = db.KhachHangs.FirstOrDefault(k => k.Email == customer.Email && k.MaKH != customer.MaKH);
                    if (existingCustomer != null)
                    {
                        ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống!");
                        return View(customer);
                    }

                    db.Entry(customer).State = EntityState.Modified;
                    db.SaveChanges();

                    TempData["SuccessMessage"] = "Cập nhật thông tin khách hàng thành công!";
                    return RedirectToAction("Customers");
                }

                return View(customer);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi cập nhật khách hàng: " + ex.Message;
                return View(customer);
            }
        }

        // Xóa khách hàng
        public ActionResult DeleteCustomer(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                var customer = db.KhachHangs.Find(id);
                if (customer == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy khách hàng để xóa!";
                    return RedirectToAction("Customers");
                }

                // Kiểm tra xem khách hàng có đơn hàng không
                var hasOrders = db.DonHangs.Any(d => d.MaKH == id);
                if (hasOrders)
                {
                    TempData["ErrorMessage"] = $"Không thể xóa khách hàng '{customer.HoTen}' vì có đơn hàng liên quan!";
                    return RedirectToAction("Customers");
                }

                // Xóa tài khoản liên quan nếu có
                var taiKhoan = db.TaiKhoans.FirstOrDefault(t => t.MaKH == id);
                if (taiKhoan != null)
                {
                    db.TaiKhoans.Remove(taiKhoan);
                }

                db.KhachHangs.Remove(customer);
                db.SaveChanges();

                TempData["SuccessMessage"] = $"Đã xóa khách hàng '{customer.HoTen}' thành công!";
                return RedirectToAction("Customers");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi xóa khách hàng: " + ex.Message;
                return RedirectToAction("Customers");
            }
        }

        // Chi tiết khách hàng
        public ActionResult CustomerDetails(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                var customer = db.KhachHangs.Find(id);
                if (customer == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy khách hàng!";
                    return RedirectToAction("Customers");
                }

                var orderHistory = db.DonHangs
                    .Where(d => d.MaKH == id)
                    .OrderByDescending(d => d.NgayDat)
                    .ToList();
                ViewBag.OrderHistory = orderHistory;

                // Tính tổng chi tiêu - chỉ tính các đơn hàng hoàn thành
                decimal totalSpent = 0;
                var completedOrders = orderHistory.Where(o => o.TrangThai == "Hoàn thành").ToList();

                foreach (var order in completedOrders)
                {
                    var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == order.MaDH).ToList();
                    decimal orderTotal = orderDetails.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0);
                    totalSpent += orderTotal + (order.PhiGiaoHang ?? 0);
                }

                ViewBag.TotalSpent = totalSpent;
                ViewBag.TotalOrders = orderHistory.Count;
                ViewBag.CompletedOrders = completedOrders.Count;

                return View(customer);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi tải thông tin khách hàng: " + ex.Message;
                return RedirectToAction("Customers");
            }
        }

        // ===========================
        // QUẢN LÝ NHÂN VIÊN - CRUD HOÀN CHỈNH
        // ===========================
        [AdminAuthorize("Admin")]
        public ActionResult Employees()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var employees = db.NhanViens.ToList();
            return View(db.NhanViens.ToList());
        }

        // Thêm nhân viên
        [AdminAuthorize("Admin")]
        public ActionResult AddEmployee()
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddEmployee(NhanVien employee)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            if (ModelState.IsValid)
            {
                db.NhanViens.Add(employee);
                db.SaveChanges();

                // Tạo tài khoản cho nhân viên - BCrypt hash
                var taiKhoan = new TaiKhoan
                {
                    TenDangNhap = "nv" + employee.MaNV,
                    MatKhau = SecurityHelper.HashPassword("123456"),
                    VaiTro = "NhanVien",
                    TrangThai = true,
                    MaNV = employee.MaNV,
                    NgayTao = DateTime.Now
                };
                db.TaiKhoans.Add(taiKhoan);
                db.SaveChanges();

                TempData["SuccessMessage"] = "Thêm nhân viên thành công! Tài khoản: nv" + employee.MaNV + " | Mật khẩu: 123456";
                return RedirectToAction("Employees");
            }

            return View(employee);
        }

        // Sửa nhân viên
        [AdminAuthorize("Admin")]
        public ActionResult EditEmployee(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var employee = db.NhanViens.Find(id);
            if (employee == null) return HttpNotFound();

            return View(employee);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditEmployee(NhanVien employee)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            if (ModelState.IsValid)
            {
                db.Entry(employee).State = EntityState.Modified;
                db.SaveChanges();
                TempData["SuccessMessage"] = "Cập nhật nhân viên thành công!";
                return RedirectToAction("Employees");
            }

            return View(employee);
        }

        // Xóa nhân viên
        [AdminAuthorize("Admin")]
        public ActionResult DeleteEmployee(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");
            var employee = db.NhanViens.Find(id);
            if (employee == null) return HttpNotFound();

            // Kiểm tra xem nhân viên có tài khoản không
            var taiKhoan = db.TaiKhoans.FirstOrDefault(t => t.MaNV == id);
            if (taiKhoan != null)
            {
                db.TaiKhoans.Remove(taiKhoan);
            }

            db.NhanViens.Remove(employee);
            db.SaveChanges();
            TempData["SuccessMessage"] = "Xóa nhân viên thành công!";
            return RedirectToAction("Employees");
        }



        // ===========================
        // QUẢN LÝ TÀI KHOẢN - CRUD HOÀN CHỈNH
        // ===========================
        [AdminAuthorize("Admin")]
        public ActionResult Accounts()
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var accounts = db.TaiKhoans
                .Include(t => t.KhachHang)
                .Include(t => t.NhanVien)
                .ToList();

            return View(db.TaiKhoans.ToList());
        }

        // Thêm tài khoản - GET
        public ActionResult AddAccount()
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            ViewBag.KhachHangList = db.KhachHangs.ToList();
            ViewBag.NhanVienList = db.NhanViens.ToList();

            return View();
        }

        // Thêm tài khoản - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddAccount(TaiKhoan account, string confirmPassword, string hoTenKH, string emailKH, string sdtKH, string diaChiKH)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // Kiểm tra mật khẩu xác nhận
                if (string.IsNullOrEmpty(account.MatKhau) || account.MatKhau != confirmPassword)
                {
                    ModelState.AddModelError("confirmPassword", "Mật khẩu xác nhận không khớp!");
                    ViewBag.NhanVienList = db.NhanViens.ToList();
                    return View(account);
                }

                // Kiểm tra tên đăng nhập đã tồn tại
                var existingAccount = db.TaiKhoans.FirstOrDefault(t => t.TenDangNhap == account.TenDangNhap);
                if (existingAccount != null)
                {
                    ModelState.AddModelError("TenDangNhap", "Tên đăng nhập đã tồn tại!");
                    ViewBag.NhanVienList = db.NhanViens.ToList();
                    return View(account);
                }

                // XỬ LÝ THÊM KHÁCH HÀNG MỚI NẾU CHỌN VAI TRÒ KHÁCH HÀNG
                if (account.VaiTro == "KhachHang")
                {
                    // Kiểm tra thông tin khách hàng bắt buộc
                    if (string.IsNullOrEmpty(hoTenKH))
                    {
                        ModelState.AddModelError("hoTenKH", "Họ tên khách hàng là bắt buộc!");
                        ViewBag.NhanVienList = db.NhanViens.ToList();
                        return View(account);
                    }

                    if (string.IsNullOrEmpty(emailKH))
                    {
                        ModelState.AddModelError("emailKH", "Email khách hàng là bắt buộc!");
                        ViewBag.NhanVienList = db.NhanViens.ToList();
                        return View(account);
                    }

                    // Kiểm tra email khách hàng đã tồn tại
                    var existingCustomer = db.KhachHangs.FirstOrDefault(k => k.Email == emailKH);
                    if (existingCustomer != null)
                    {
                        ModelState.AddModelError("emailKH", "Email khách hàng đã tồn tại trong hệ thống!");
                        ViewBag.NhanVienList = db.NhanViens.ToList();
                        return View(account);
                    }

                    // Tạo khách hàng mới
                    var newCustomer = new KhachHang
                    {
                        HoTen = hoTenKH,
                        Email = emailKH,
                        SoDienThoai = sdtKH,
                        DiaChi = diaChiKH
                    };

                    db.KhachHangs.Add(newCustomer);
                    db.SaveChanges(); // Lưu để lấy MaKH

                    // Gán MaKH cho tài khoản
                    account.MaKH = newCustomer.MaKH;
                    account.MaNV = null; // Đảm bảo MaNV là null
                }
                else if (account.VaiTro == "NhanVien")
                {
                    // Kiểm tra đã chọn nhân viên
                    if (!account.MaNV.HasValue)
                    {
                        ModelState.AddModelError("MaNV", "Phải chọn nhân viên!");
                        ViewBag.NhanVienList = db.NhanViens.ToList();
                        return View(account);
                    }
                    account.MaKH = null; // Đảm bảo MaKH là null
                }

                // Set thông tin mặc định - BCrypt hash mật khẩu
                account.TrangThai = true;
                account.NgayTao = DateTime.Now;
                account.MatKhau = SecurityHelper.HashPassword(account.MatKhau);

                if (ModelState.IsValid)
                {
                    db.TaiKhoans.Add(account);
                    db.SaveChanges();

                    string message = $"Thêm tài khoản '{account.TenDangNhap}' thành công!";
                    if (account.VaiTro == "KhachHang")
                    {
                        message += $" Đã tạo mới khách hàng: {hoTenKH}";
                    }

                    TempData["SuccessMessage"] = message;
                    return RedirectToAction("Accounts");
                }

                ViewBag.NhanVienList = db.NhanViens.ToList();
                return View(account);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi thêm tài khoản: " + ex.Message;
                ViewBag.NhanVienList = db.NhanViens.ToList();
                return View(account);
            }
        }

        // Sửa tài khoản - GET
        public ActionResult EditAccount(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            var account = db.TaiKhoans.Find(id);
            if (account == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy tài khoản!";
                return RedirectToAction("Accounts");
            }

            ViewBag.KhachHangList = db.KhachHangs.ToList();
            ViewBag.NhanVienList = db.NhanViens.ToList();

            return View(account);
        }

        // Sửa tài khoản - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditAccount(TaiKhoan account, string newPassword)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // Kiểm tra tên đăng nhập đã tồn tại (trừ chính nó)
                var existingAccount = db.TaiKhoans.FirstOrDefault(t => t.TenDangNhap == account.TenDangNhap && t.MaTK != account.MaTK);
                if (existingAccount != null)
                {
                    ModelState.AddModelError("TenDangNhap", "Tên đăng nhập đã tồn tại!");
                    ViewBag.KhachHangList = db.KhachHangs.ToList();
                    ViewBag.NhanVienList = db.NhanViens.ToList();
                    return View(account);
                }

                // Kiểm tra chỉ được chọn MaKH HOẶC MaNV
                if (account.MaKH.HasValue && account.MaNV.HasValue)
                {
                    ModelState.AddModelError("MaKH", "Chỉ được chọn Khách hàng HOẶC Nhân viên, không được chọn cả hai!");
                    ViewBag.KhachHangList = db.KhachHangs.ToList();
                    ViewBag.NhanVienList = db.NhanViens.ToList();
                    return View(account);
                }

                var existing = db.TaiKhoans.Find(account.MaTK);
                if (existing != null)
                {
                    existing.TenDangNhap = account.TenDangNhap;

                    // Chỉ cập nhật mật khẩu nếu có nhập mới - BCrypt hash
                    if (!string.IsNullOrEmpty(newPassword))
                    {
                        existing.MatKhau = SecurityHelper.HashPassword(newPassword);
                    }

                    existing.VaiTro = account.VaiTro;
                    existing.TrangThai = account.TrangThai;
                    existing.MaKH = account.MaKH;
                    existing.MaNV = account.MaNV;

                    db.SaveChanges();

                    TempData["SuccessMessage"] = "Cập nhật tài khoản thành công!";
                    return RedirectToAction("Accounts");
                }

                TempData["ErrorMessage"] = "Không tìm thấy tài khoản để cập nhật!";
                return RedirectToAction("Accounts");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi cập nhật tài khoản: " + ex.Message;
                ViewBag.KhachHangList = db.KhachHangs.ToList();
                ViewBag.NhanVienList = db.NhanViens.ToList();
                return View(account);
            }
        }

        // Xóa tài khoản
        public ActionResult DeleteAccount(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                var account = db.TaiKhoans.Find(id);
                if (account == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy tài khoản để xóa!";
                    return RedirectToAction("Accounts");
                }

                // Không cho xóa tài khoản admin
                if (account.TenDangNhap == "admin")
                {
                    TempData["ErrorMessage"] = "Không thể xóa tài khoản admin!";
                    return RedirectToAction("Accounts");
                }

                db.TaiKhoans.Remove(account);
                db.SaveChanges();

                TempData["SuccessMessage"] = $"Đã xóa tài khoản '{account.TenDangNhap}' thành công!";
                return RedirectToAction("Accounts");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi xóa tài khoản: " + ex.Message;
                return RedirectToAction("Accounts");
            }
        }

        // Reset mật khẩu
        public ActionResult ResetPassword(int id)
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                var account = db.TaiKhoans.Find(id);
                if (account == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy tài khoản!";
                    return RedirectToAction("Accounts");
                }

                // Reset về mật khẩu mặc định - BCrypt hash
                account.MatKhau = SecurityHelper.HashPassword("123456");
                db.SaveChanges();

                TempData["SuccessMessage"] = $"Đã reset mật khẩu tài khoản '{account.TenDangNhap}' về '123456'!";
                return RedirectToAction("Accounts");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi reset mật khẩu: " + ex.Message;
                return RedirectToAction("Accounts");
            }
        }




        // ===========================
        // THỐNG KÊ 
        // ===========================
        public ActionResult Reports()
        {
            if (!CheckLogin()) return RedirectToAction("Login");

            try
            {
                // THỐNG KÊ TỔNG QUAN
                ViewBag.TongDonHang = db.DonHangs.Count();
                ViewBag.TongKhachHang = db.KhachHangs.Count();
                ViewBag.DonHangChoXuLy = db.DonHangs.Count(d => d.TrangThai == "Chờ xử lý");
                ViewBag.TongSanPham = db.SanPhams.Count();

                // THÊM CÁC THỐNG KÊ THỰC TẾ
                ViewBag.DonHangDangGiao = db.DonHangs.Count(d => d.TrangThai == "Đang giao");
                ViewBag.DonHangHoanThanh = db.DonHangs.Count(d => d.TrangThai == "Hoàn thành");
                ViewBag.DonHangDaHuy = db.DonHangs.Count(d => d.TrangThai == "Đã hủy");

                // TÍNH TỔNG DOANH THU THỰC TẾ
                decimal totalRevenue = 0;
                var completedOrders = db.DonHangs.Where(d => d.TrangThai == "Giao hàng thành công").ToList();
                foreach (var order in completedOrders)
                {
                    var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == order.MaDH).ToList();
                    totalRevenue += orderDetails.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0);
                }
                ViewBag.TongDoanhThu = totalRevenue;

                // TÍNH ĐÁNH GIÁ TRUNG BÌNH THỰC TẾ
                var danhGiaList = db.DanhGias.ToList();
                double diemTrungBinh = 0;
                if (danhGiaList.Any())
                {
                    diemTrungBinh = danhGiaList.Average(d => d.Diem ?? 0);
                }
                ViewBag.DiemTrungBinh = Math.Round(diemTrungBinh, 1);

                // TÍNH GIÁ TRỊ TRUNG BÌNH MỖI ĐƠN HÀNG
                decimal giaTriTrungBinh = 0;
                if (completedOrders.Any())
                {
                    giaTriTrungBinh = totalRevenue / completedOrders.Count;
                }
                ViewBag.GiaTriTrungBinh = giaTriTrungBinh;

                // DOANH THU THEO THÁNG - FIX HOÀN TOÀN
                var currentYear = DateTime.Now.Year;
                var monthlyRevenue = new decimal[12];

                // Khởi tạo tất cả các tháng = 0
                for (int i = 0; i < 12; i++)
                {
                    monthlyRevenue[i] = 0;
                }

                // Lấy tất cả đơn hàng hoàn thành trong năm hiện tại
                var firstDayOfYear = new DateTime(currentYear, 1, 1);
                var lastDayOfYear = new DateTime(currentYear, 12, 31);

                var ordersThisYear = db.DonHangs
                    .Where(d => d.NgayDat >= firstDayOfYear &&
                               d.NgayDat <= lastDayOfYear &&
                               d.TrangThai == "Giao hàng thành công")
                    .ToList();

                // Tính doanh thu cho từng tháng
                foreach (var order in ordersThisYear)
                {
                    if (order.NgayDat.HasValue)
                    {
                        int month = order.NgayDat.Value.Month - 1; // Chuyển về index 0-11
                        if (month >= 0 && month < 12)
                        {
                            var orderDetails = db.ChiTietDonHangs.Where(ct => ct.MaDH == order.MaDH).ToList();
                            decimal orderTotal = orderDetails.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0);
                            monthlyRevenue[month] += orderTotal;
                        }
                    }
                }

                ViewBag.MonthlyRevenue = monthlyRevenue;

                // TOP SẢN PHẨM BÁN CHẠY
                var topProductsQuery = db.ChiTietDonHangs
                    .Include(ct => ct.DonHang)
                    .Where(ct => ct.DonHang.TrangThai == "Giao hàng thành công")
                    .GroupBy(ct => ct.MaSP)
                    .Select(g => new
                    {
                        MaSP = g.Key,
                        TotalSold = g.Sum(ct => ct.SoLuong ?? 0),
                        TotalRevenue = g.Sum(ct => (ct.SoLuong * ct.DonGia) ?? 0)
                    })
                    .OrderByDescending(x => x.TotalSold)
                    .Take(5)
                    .ToList();

                // Tạo lists riêng
                var productNames = new List<string>();
                var productSales = new List<int>();
                var productRevenues = new List<decimal>();

                foreach (var item in topProductsQuery)
                {
                    var product = db.SanPhams.FirstOrDefault(p => p.MaSP == item.MaSP);
                    if (product != null)
                    {
                        productNames.Add(product.TenSP);
                        productSales.Add(item.TotalSold);
                        productRevenues.Add(item.TotalRevenue);
                    }
                }

                ViewBag.TopProductNames = productNames;
                ViewBag.TopProductSales = productSales;
                ViewBag.TopProductRevenues = productRevenues;

                // ĐƠN HÀNG GẦN ĐÂY
                ViewBag.DonHangGanDay = db.DonHangs
                    .Include(d => d.KhachHang)
                    .Include(d => d.ChiTietDonHangs)
                    .OrderByDescending(d => d.NgayDat)
                    .Take(5)
                    .ToList();

                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Lỗi khi tải thống kê: " + ex.Message;
                // Trả về giá trị mặc định
                ViewBag.MonthlyRevenue = new decimal[12];
                ViewBag.TopProductNames = new List<string>();
                ViewBag.TopProductSales = new List<int>();
                ViewBag.TopProductRevenues = new List<decimal>();
                ViewBag.DonHangGanDay = new List<DonHang>();
                ViewBag.DonHangDangGiao = 0;
                ViewBag.DonHangHoanThanh = 0;
                ViewBag.DonHangDaHuy = 0;
                ViewBag.TongSanPham = 0;
                ViewBag.DiemTrungBinh = 0;
                ViewBag.GiaTriTrungBinh = 0;
                return View();
            }
        }
        // Đăng xuất
        public ActionResult Logout()
        {
            Session.Clear();
            return RedirectToAction("Login");
        }
    }


}