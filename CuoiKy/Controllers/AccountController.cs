using CuoiKy.Models;
using CuoiKy.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Validation;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace CuoiKy.Controllers
{
    public class AccountController : Controller
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Account/Index
        public ActionResult Index()
        {
            // Kiểm tra đăng nhập qua Session
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];
            string tenKhachHang = Session["TenKhachHang"].ToString();

            // Lấy thông tin đơn hàng
            var donHangs = db.DonHangs
                .Where(d => d.MaKH == maKH)
                .Include(d => d.ChiTietDonHangs)
                .ToList();

            // Tính toán thống kê
            int orderCount = donHangs.Count();
            int pendingOrders = donHangs.Count(d => d.TrangThai == "Chờ xử lý");
            int shippingOrders = donHangs.Count(d => d.TrangThai == "Đang giao");
            int completedOrders = donHangs.Count(d => d.TrangThai == "Hoàn thành");

            // Lấy 5 đơn hàng gần đây
            var recentOrders = donHangs
                .OrderByDescending(d => d.NgayDat)
                .Take(5)
                .Select(d => new RecentOrderViewModel
                {
                    MaDH = d.MaDH,
                    NgayDat = d.NgayDat,
                    SoLuong = d.ChiTietDonHangs.Sum(ct => ct.SoLuong),
                    TongTien = d.ChiTietDonHangs.Sum(ct => ct.SoLuong * ct.DonGia) + (d.PhiGiaoHang ?? 0),
                    TrangThai = d.TrangThai
                })
                .ToList();

            // Truyền dữ liệu qua ViewBag và ViewData
            ViewBag.TenKhachHang = tenKhachHang;
            ViewBag.OrderCount = orderCount;
            ViewBag.PendingOrders = pendingOrders;
            ViewBag.ShippingOrders = shippingOrders;
            ViewBag.CompletedOrders = completedOrders;
            ViewBag.RecentOrders = recentOrders;

            return View();
        }

        // GET: Account/Profile
        public ActionResult Profile()
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];
            var khachHang = db.KhachHangs.Find(maKH);

            if (khachHang == null)
            {
                return HttpNotFound();
            }

            // Lấy TenDangNhap từ bảng TaiKhoan
            var taiKhoan = db.TaiKhoans.FirstOrDefault(t => t.MaKH == maKH);
            if (taiKhoan != null)
            {
                ViewBag.TenDangNhap = taiKhoan.TenDangNhap;
                // Hoặc có thể lưu vào Session để dùng ở các trang khác
                Session["TenDangNhap"] = taiKhoan.TenDangNhap;
            }

            // Cập nhật Session nếu chưa có
            if (Session["Email"] == null)
            {
                Session["Email"] = khachHang.Email;
            }

            return View(khachHang);
        }

        // POST: Account/Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Profile(KhachHang model)
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];

            if (ModelState.IsValid)
            {
                // Kiểm tra quyền sở hữu
                if (model.MaKH != maKH)
                {
                    return new HttpStatusCodeResult(403, "Không có quyền truy cập");
                }

                // Lấy khách hàng từ DB và cập nhật
                var khachHang = db.KhachHangs.Find(model.MaKH);
                if (khachHang == null)
                {
                    return HttpNotFound();
                }

                // Cập nhật thông tin
                khachHang.HoTen = model.HoTen;
                khachHang.Email = model.Email;
                khachHang.SoDienThoai = model.SoDienThoai;
                khachHang.DiaChi = model.DiaChi;

                db.Entry(khachHang).State = EntityState.Modified;
                db.SaveChanges();

                // Cập nhật Session
                Session["TenKhachHang"] = khachHang.HoTen;
                Session["Email"] = khachHang.Email;

                // Lấy lại TenDangNhap để hiển thị
                var taiKhoan = db.TaiKhoans.FirstOrDefault(t => t.MaKH == maKH);
                if (taiKhoan != null)
                {
                    ViewBag.TenDangNhap = taiKhoan.TenDangNhap;
                }

                ViewBag.SuccessMessage = "Cập nhật thông tin thành công!";
                return View(khachHang);
            }

            // Nếu validation fail, vẫn cần lấy TenDangNhap
            var taiKhoanForView = db.TaiKhoans.FirstOrDefault(t => t.MaKH == maKH);
            if (taiKhoanForView != null)
            {
                ViewBag.TenDangNhap = taiKhoanForView.TenDangNhap;
            }

            return View(model);
        }


        // GET: Account/Orders
        public ActionResult Orders()
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];

            var donHangs = db.DonHangs
                .Where(d => d.MaKH == maKH)
                .OrderByDescending(d => d.NgayDat)
                .Include(d => d.ChiTietDonHangs.Select(ct => ct.SanPham))
                .ToList();

            return View(donHangs);
        }

        // GET: Account/OrderDetail/{id}
        public ActionResult OrderDetail(int id)
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];

            var donHang = db.DonHangs
                .Include(d => d.ChiTietDonHangs.Select(ct => ct.SanPham))
                .Include(d => d.KhachHang) // Thêm để lấy thông tin khách hàng
                .Include(d => d.NguoiNhans) // Thêm để lấy thông tin người nhận
                .Include(d => d.ThanhToans) // Thêm để lấy thông tin thanh toán
                .FirstOrDefault(d => d.MaDH == id);

            if (donHang == null)
            {
                return HttpNotFound();
            }

           

            // Không cần ViewBag.NguoiNhan vì đã include trong model
            return View(donHang);
        }

        // GET: Account/ChangePassword
        public ActionResult ChangePassword()
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            return View();
        }

        // POST: Account/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangePassword(ChangePasswordViewModel model)
        {
            if (Session["MaKH"] == null)
            {
                return RedirectToAction("Login", "User");
            }

            int maKH = (int)Session["MaKH"];

            if (ModelState.IsValid)
            {
                // Lấy tài khoản
                var taiKhoan = db.TaiKhoans.FirstOrDefault(t => t.MaKH == maKH);
                if (taiKhoan == null)
                {
                    ModelState.AddModelError("", "Không tìm thấy tài khoản");
                    return View(model);
                }

                // Kiểm tra mật khẩu cũ - BCrypt hoặc backward compatibility
                bool oldPasswordValid = false;
                if (SecurityHelper.IsBcryptHash(taiKhoan.MatKhau))
                    oldPasswordValid = SecurityHelper.VerifyPassword(model.OldPassword, taiKhoan.MatKhau);
                else
                    oldPasswordValid = taiKhoan.MatKhau == model.OldPassword;

                if (!oldPasswordValid)
                {
                    ModelState.AddModelError("OldPassword", "Mật khẩu cũ không chính xác");
                    return View(model);
                }

                // Kiểm tra mật khẩu mới không trùng với mật khẩu cũ
                if (model.NewPassword == model.OldPassword)
                {
                    ModelState.AddModelError("NewPassword", "Mật khẩu mới không được trùng với mật khẩu cũ");
                    return View(model);
                }

                // Cập nhật mật khẩu - BCrypt hash, không lưu dạng text
                taiKhoan.MatKhau = SecurityHelper.HashPassword(model.NewPassword);
                db.Entry(taiKhoan).State = EntityState.Modified;
                db.SaveChanges();

                ViewBag.SuccessMessage = "Đổi mật khẩu thành công!";
                return View();
            }

            return View(model);
        }
        // POST: Account/CancelOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CancelOrder()
        {
            try
            {
                // 1. Lấy id đơn hàng từ form
                int id;
                if (!int.TryParse(Request.Form["id"], out id) || id <= 0)
                {
                    return Json(new { success = false, message = "ID đơn hàng không hợp lệ" });
                }

                // 2. Kiểm tra đăng nhập
                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập" });
                }

                int maKH = (int)Session["MaKH"];

                // 3. Tìm đơn hàng
                var donHang = db.DonHangs.FirstOrDefault(d => d.MaDH == id && d.MaKH == maKH);

                if (donHang == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // 4. Chỉ hủy nếu ở trạng thái "Chờ xác nhận"
                if (donHang.TrangThai != "Chờ xác nhận")
                {
                    return Json(new { success = false, message = "Chỉ có thể hủy đơn hàng ở trạng thái 'Chờ xác nhận'" });
                }

                // 5. CẬP NHẬT TRẠNG THÁI THÀNH "ĐÃ HỦY"
                donHang.TrangThai = "Đã hủy";

                // 6. Có thể cập nhật ngày chỉnh sửa nếu có cột này
                // donHang.NgayCapNhat = DateTime.Now; // (nếu có cột NgayCapNhat)

                db.Entry(donHang).State = EntityState.Modified;
                db.SaveChanges();

                return Json(new { success = true, message = "Đã hủy đơn hàng thành công" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // GET: Account/Logout
        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();
            FormsAuthentication.SignOut();
            TempData["InfoMessage"] = "Bạn đã đăng xuất thành công.";
            return RedirectToAction("Index", "EvolStore");
        }

        // GET: Account/AutoLogout - dùng cho kịch bản tự động đăng xuất sau 5 phút không hoạt động
        public ActionResult AutoLogout()
        {
            Session.Clear();
            Session.Abandon();
            FormsAuthentication.SignOut();
            // Dùng query string để chắc chắn thông báo không bị mất qua redirect
            return RedirectToAction("Index", "EvolStore", new { autoLogout = 1 });
        }
    }

    // ViewModel cho đổi mật khẩu
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu cũ")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu cũ")]
        public string OldPassword { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu mới")]
        [System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        public string ConfirmPassword { get; set; }
    }

    // ViewModel cho đơn hàng gần đây
    public class RecentOrderViewModel
    {
        public int MaDH { get; set; }
        public DateTime? NgayDat { get; set; }
        public int? SoLuong { get; set; }
        public decimal? TongTien { get; set; }
        public string TrangThai { get; set; }
    }
}