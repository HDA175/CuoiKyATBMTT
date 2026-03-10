using CuoiKy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Data.Entity;

namespace CuoiKy.Controllers
{
    public class EvolStoreController : BaseController
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Giỏ hàng
        public ActionResult GioHang()
        {
            var gioHang = Session["GioHang"] as List<GioHang> ?? new List<GioHang>();
            return View(gioHang);
        }

        // POST: Thêm sản phẩm vào giỏ hàng
        [HttpPost]
        public ActionResult ThemVaoGioHang(int maSP, int soLuong = 1, string size = "M", string mauSac = "Đen")
        {
            var sanPham = db.SanPhams.Find(maSP);
            if (sanPham == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại" });
            }

            var gioHang = Session["GioHang"] as List<GioHang> ?? new List<GioHang>();

            // Kiểm tra sản phẩm đã có trong giỏ hàng chưa
            var existingItem = gioHang.FirstOrDefault(item => item.MaSP == maSP && item.Size == size && item.MauSac == mauSac);

            if (existingItem != null)
            {
                // Nếu đã có thì tăng số lượng
                existingItem.SoLuong += soLuong;
            }
            else
            {
                // Nếu chưa có thì thêm mới
                var newItem = new GioHang
                {
                    MaSP = maSP,
                    TenSP = sanPham.TenSP,
                    Gia = sanPham.Gia,
                    SoLuong = soLuong,
                    Size = size,
                    MauSac = mauSac,
                    HinhAnh = "/images/lvents1.jpg", // Default image
                    CoTheMua = sanPham.SoLuongTon > 0
                };
                gioHang.Add(newItem);
            }

            Session["GioHang"] = gioHang;

            return Json(new
            {
                success = true,
                message = "Đã thêm vào giỏ hàng",
                cartCount = gioHang.Sum(item => item.SoLuong)
            });
        }

        // POST: Cập nhật số lượng
        [HttpPost]
        public ActionResult CapNhatSoLuong(int maSP, string size, string mauSac, int soLuong)
        {
            var gioHang = Session["GioHang"] as List<GioHang>;
            if (gioHang == null)
                return Json(new { success = false, message = "Giỏ hàng trống" });

            var item = gioHang.FirstOrDefault(i => i.MaSP == maSP && i.Size == size && i.MauSac == mauSac);
            if (item != null)
            {
                item.SoLuong = soLuong;
                Session["GioHang"] = gioHang;

                var tongTien = gioHang.Sum(i => i.ThanhTien);
                var itemTotal = item.ThanhTien;

                return Json(new
                {
                    success = true,
                    itemTotal = itemTotal.ToString("N0"),
                    tongTien = tongTien.ToString("N0"),
                    cartCount = gioHang.Sum(i => i.SoLuong)
                });
            }

            return Json(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng" });
        }

        // POST: Xóa sản phẩm khỏi giỏ hàng
        [HttpPost]
        public ActionResult XoaKhoiGioHang(int maSP, string size, string mauSac)
        {
            var gioHang = Session["GioHang"] as List<GioHang>;
            if (gioHang == null)
                return Json(new { success = false, message = "Giỏ hàng trống" });

            var item = gioHang.FirstOrDefault(i => i.MaSP == maSP && i.Size == size && i.MauSac == mauSac);
            if (item != null)
            {
                gioHang.Remove(item);
                Session["GioHang"] = gioHang;

                var tongTien = gioHang.Sum(i => i.ThanhTien);

                return Json(new
                {
                    success = true,
                    tongTien = tongTien.ToString("N0"),
                    cartCount = gioHang.Sum(i => i.SoLuong)
                });
            }

            return Json(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng" });
        }

        // Lấy số lượng sản phẩm trong giỏ hàng
        [HttpPost]
        public ActionResult LaySoLuongGioHang()
        {
            var gioHang = Session["GioHang"] as List<GioHang> ?? new List<GioHang>();
            return Json(new { count = gioHang.Sum(item => item.SoLuong) });
        }

        public ActionResult CuaHang(string loai = "", string chatLieu = "", string mucGia = "", string search = "")
        {
            var sanPhams = db.SanPhams.AsQueryable();

            // Lọc theo loại sản phẩm
            if (!string.IsNullOrEmpty(loai))
            {
                switch (loai.ToLower())
                {
                    case "ao":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Áo") &&
                                                       !sp.Loai.Contains("Áo khoác") &&
                                                       !sp.TenSP.ToLower().Contains("khoác"));
                        break;
                    case "quan":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Quần"));
                        break;
                    case "giay-dep":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Giày"));
                        break;
                    case "phu-kien":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Phụ kiện"));
                        break;
                    case "vay-dam":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Váy") || sp.Loai.Contains("Đầm"));
                        break;
                    case "ao-khoac":
                        sanPhams = sanPhams.Where(sp => sp.Loai.Contains("Áo khoác"));
                        break;
                }
            }

            // Lọc theo chất liệu
            if (!string.IsNullOrEmpty(chatLieu))
            {
                switch (chatLieu.ToLower())
                {
                    case "cotton":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("cotton"));
                        break;
                    case "denim":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("denim"));
                        break;
                    case "polyester":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("polyester"));
                        break;
                    case "da":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("da"));
                        break;
                    case "ni":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("nỉ"));
                        break;
                    case "lua":
                        sanPhams = sanPhams.Where(sp => sp.ChatLieu.ToLower().Contains("lụa"));
                        break;
                }
            }

            // Lọc theo mức giá
            if (!string.IsNullOrEmpty(mucGia))
            {
                switch (mucGia.ToLower())
                {
                    case "duoi-200k":
                        sanPhams = sanPhams.Where(sp => sp.Gia < 200000);
                        break;
                    case "200k-500k":
                        sanPhams = sanPhams.Where(sp => sp.Gia >= 200000 && sp.Gia <= 500000);
                        break;
                    case "tren-500k":
                        sanPhams = sanPhams.Where(sp => sp.Gia > 500000);
                        break;
                }
            }

            // Tìm kiếm
            if (!string.IsNullOrEmpty(search))
            {
                sanPhams = sanPhams.Where(sp => sp.TenSP.Contains(search) ||
                                               sp.Loai.Contains(search) ||
                                               sp.ChatLieu.Contains(search));
            }

            // Sắp xếp và lấy dữ liệu
            sanPhams = sanPhams.Where(sp => sp.SoLuongTon > 0)
                              .OrderBy(sp => sp.MaSP);

            ViewBag.SelectedLoai = loai;
            ViewBag.SelectedChatLieu = chatLieu;
            ViewBag.SelectedMucGia = mucGia;
            ViewBag.SearchTerm = search;

            return View(sanPhams.ToList());
        }
        public ActionResult ChiTietSanPham(int id)
        {
            // Lấy sản phẩm theo ID từ database
            var sanPham = db.SanPhams.FirstOrDefault(sp => sp.MaSP == id);

            if (sanPham == null)
            {
                return HttpNotFound();
            }

            // Lấy các sản phẩm liên quan (cùng loại)
            var sanPhamLienQuan = db.SanPhams
                .Where(sp => sp.Loai == sanPham.Loai && sp.MaSP != id)
                .Take(4)
                .ToList();

            // Lấy danh sách đánh giá cho sản phẩm (không cần kiểm tra TrangThai)
            var danhGia = db.DanhGias
                .Where(dg => dg.MaSP == id)
                .Include(dg => dg.KhachHang)
                .OrderByDescending(dg => dg.MaDG)
                .ToList();

            // Tính điểm trung bình (không cần kiểm tra TrangThai)
            var diemTrungBinh = 0.0;
            var tongDanhGia = 0;

            if (danhGia.Any(d => d.Diem.HasValue))
            {
                diemTrungBinh = danhGia.Where(d => d.Diem.HasValue).Average(d => d.Diem.Value);
                tongDanhGia = danhGia.Count;
            }

            ViewBag.SanPhamLienQuan = sanPhamLienQuan;
            ViewBag.DanhGia = danhGia;
            ViewBag.DiemTrungBinh = Math.Round(diemTrungBinh, 1);
            ViewBag.TongDanhGia = tongDanhGia;

            return View(sanPham);
        }
        // Action để thêm đánh giá mới
        
        [HttpPost]
        public JsonResult ThemDanhGia(int maSP, string noiDung, int diem)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== BẮT ĐẦU THEMDANHGIA ===");
                System.Diagnostics.Debug.WriteLine($"maSP: {maSP}, noiDung: {noiDung}, diem: {diem}");

                // Kiểm tra đăng nhập
                if (Session["MaKH"] == null)
                {
                    System.Diagnostics.Debug.WriteLine("LỖI: Chưa đăng nhập");
                    return Json(new { success = false, message = "Vui lòng đăng nhập để đánh giá!" });
                }

                int maKH = (int)Session["MaKH"];
                System.Diagnostics.Debug.WriteLine($"MaKH từ Session: {maKH}");

                // Kiểm tra dữ liệu
                if (string.IsNullOrEmpty(noiDung))
                {
                    return Json(new { success = false, message = "Vui lòng nhập nội dung đánh giá!" });
                }

                if (diem < 1 || diem > 5)
                {
                    return Json(new { success = false, message = "Điểm đánh giá không hợp lệ!" });
                }

                // Kiểm tra sản phẩm tồn tại
                var sanPham = db.SanPhams.Find(maSP);
                if (sanPham == null)
                {
                    System.Diagnostics.Debug.WriteLine("LỖI: Sản phẩm không tồn tại");
                    return Json(new { success = false, message = "Sản phẩm không tồn tại!" });
                }

                // Kiểm tra khách hàng tồn tại
                var khachHang = db.KhachHangs.Find(maKH);
                if (khachHang == null)
                {
                    System.Diagnostics.Debug.WriteLine("LỖI: Khách hàng không tồn tại");
                    return Json(new { success = false, message = "Khách hàng không tồn tại!" });
                }

                System.Diagnostics.Debug.WriteLine("Tạo đánh giá mới...");

                // Tạo đánh giá mới
                var danhGia = new DanhGia
                {
                    MaSP = maSP,
                    MaKH = maKH,
                    NoiDung = noiDung,
                    Diem = diem
                };

                System.Diagnostics.Debug.WriteLine("Thêm vào database...");
                db.DanhGias.Add(danhGia);
                db.SaveChanges();
                System.Diagnostics.Debug.WriteLine("Lưu thành công!");

                // Tính lại điểm trung bình
                var avgRating = db.DanhGias
                    .Where(d => d.MaSP == maSP)
                    .Average(d => (double?)d.Diem) ?? 0;

                var totalReviews = db.DanhGias
                    .Count(d => d.MaSP == maSP);

                System.Diagnostics.Debug.WriteLine($"Kết quả: avgRating={avgRating}, totalReviews={totalReviews}");

                return Json(new
                {
                    success = true,
                    message = "Đã gửi đánh giá thành công!",
                    averageRating = Math.Round(avgRating, 1),
                    totalReviews = totalReviews
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("=== LỖI CHI TIẾT ===");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return Json(new
                {
                    success = false,
                    message = $"Lỗi: {ex.Message}" // Hiển thị lỗi cụ thể
                });
            }
        }
        [HttpPost]
        public JsonResult XoaDanhGia(int maDG)
        {
            try
            {
                // Kiểm tra đăng nhập
                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập!" });
                }

                int maKH = (int)Session["MaKH"];

                // Tìm đánh giá
                var danhGia = db.DanhGias.FirstOrDefault(d => d.MaDG == maDG);

                if (danhGia == null)
                {
                    return Json(new { success = false, message = "Đánh giá không tồn tại!" });
                }

                // Kiểm tra người dùng có quyền xóa không (chỉ xóa của chính mình)
                if (danhGia.MaKH != maKH)
                {
                    return Json(new { success = false, message = "Bạn không có quyền xóa đánh giá này!" });
                }

                // Xóa đánh giá
                db.DanhGias.Remove(danhGia);
                db.SaveChanges();

                // Tính lại điểm trung bình
                var avgRating = db.DanhGias
                    .Where(d => d.MaSP == danhGia.MaSP)
                    .Average(d => (double?)d.Diem) ?? 0;

                var totalReviews = db.DanhGias
                    .Count(d => d.MaSP == danhGia.MaSP);

                return Json(new
                {
                    success = true,
                    message = "Đã xóa đánh giá thành công!",
                    averageRating = Math.Round(avgRating, 1),
                    totalReviews = totalReviews
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LỖI XoaDanhGia: " + ex.Message);
                return Json(new
                {
                    success = false,
                    message = "Có lỗi xảy ra khi xóa đánh giá!"
                });
            }
        }
        [HttpPost]
        public JsonResult SuaDanhGia(int maDG, string noiDung, int diem)
        {
            try
            {
                // Kiểm tra đăng nhập
                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập!" });
                }

                int maKH = (int)Session["MaKH"];

                // Tìm đánh giá
                var danhGia = db.DanhGias.FirstOrDefault(d => d.MaDG == maDG);

                if (danhGia == null)
                {
                    return Json(new { success = false, message = "Đánh giá không tồn tại!" });
                }

                // Kiểm tra người dùng có quyền sửa không (chỉ sửa của chính mình)
                if (danhGia.MaKH != maKH)
                {
                    return Json(new { success = false, message = "Bạn không có quyền sửa đánh giá này!" });
                }

                // Kiểm tra dữ liệu
                if (string.IsNullOrEmpty(noiDung))
                {
                    return Json(new { success = false, message = "Vui lòng nhập nội dung đánh giá!" });
                }

                if (diem < 1 || diem > 5)
                {
                    return Json(new { success = false, message = "Điểm đánh giá không hợp lệ!" });
                }

                // Cập nhật đánh giá
                danhGia.NoiDung = noiDung;
                danhGia.Diem = diem;
                // Có thể thêm NgaySua nếu muốn theo dõi thời gian sửa

                db.SaveChanges();

                // Tính lại điểm trung bình
                var avgRating = db.DanhGias
                    .Where(d => d.MaSP == danhGia.MaSP)
                    .Average(d => (double?)d.Diem) ?? 0;

                var totalReviews = db.DanhGias
                    .Count(d => d.MaSP == danhGia.MaSP);

                return Json(new
                {
                    success = true,
                    message = "Đã cập nhật đánh giá thành công!",
                    averageRating = Math.Round(avgRating, 1),
                    totalReviews = totalReviews,
                    updatedReview = new
                    {
                        noiDung = danhGia.NoiDung,
                        diem = danhGia.Diem
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LỖI SuaDanhGia: " + ex.Message);
                return Json(new
                {
                    success = false,
                    message = "Có lỗi xảy ra khi sửa đánh giá!"
                });
            }
        }
        // Action để lấy danh sách đánh giá
        public ActionResult LayDanhGia(int maSP)
        {
            var danhGia = db.DanhGias
        .Where(dg => dg.MaSP == maSP)
        .Include(dg => dg.KhachHang)
        .OrderByDescending(dg => dg.MaDG)
        .Select(dg => new
        {
            TenKhachHang = dg.KhachHang != null ? dg.KhachHang.HoTen : "Khách hàng",
            NoiDung = dg.NoiDung,
            Diem = dg.Diem ?? 0,
            NgayTao = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
        })
        .ToList();

            return Json(danhGia, JsonRequestBehavior.AllowGet);
        }
        //GET: EvolStore
        public ActionResult Index()
        {
            // Lấy 12 sản phẩm bán chạy (nhiều hơn để có thể slide)
            var sanPhamBanChay = db.SanPhams
                .Where(sp => sp.SoLuongTon > 0)
                .OrderByDescending(sp => sp.MaSP)
                .Take(12)
                .ToList();

            // Lấy 12 sản phẩm khác để hiển thị ở mục "Đang sale"
            var sanPhamSale = db.SanPhams
                .Where(sp => sp.SoLuongTon > 0)
                .OrderBy(sp => sp.MaSP)
                .Take(12)
                .ToList();

            ViewBag.SanPhamBanChay = sanPhamBanChay;
            ViewBag.SanPhamSale = sanPhamSale;

            return View();
        }

        public ActionResult NavPartial()
        {
            return PartialView();
        }

        public ActionResult FooterPartial()
        {
            return PartialView();
        }

        public ActionResult GioiThieu()
        {
            return View();
        }

        public ActionResult LienHe()
        {
            return View();
        }
    }
}