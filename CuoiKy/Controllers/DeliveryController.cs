using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using CuoiKy.Models;

namespace CuoiKy.Controllers
{
    public class DeliveryController : Controller
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Delivery - Danh sách đơn hàng cần giao
        // GET: Delivery - Danh sách đơn hàng cần giao
        public ActionResult Index(string searchString, string trangThai)
        {
            var query = db.DonHangs
                .Include(d => d.KhachHang)
                .Include(d => d.NguoiNhans)
                .Include(d => d.ThanhToans)
                .Where(d => d.TrangThai != "Chờ xác nhận")
                .AsQueryable();

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(trangThai))
            {
                query = query.Where(d => d.TrangThai == trangThai);
            }

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                // Thử chuyển searchString thành số để tìm theo mã đơn hàng
                if (int.TryParse(searchString, out int maDH))
                {
                    query = query.Where(d => d.MaDH == maDH);
                }
                else
                {
                    // Tìm theo tên khách hàng hoặc tên người nhận
                    query = query.Where(d =>
                        d.KhachHang.HoTen.Contains(searchString) ||
                        d.NguoiNhans.Any(n => n.HoTen.Contains(searchString))
                    );
                }
            }

            var deliveries = query
                .OrderByDescending(d => d.NgayDat)
                .ToList();

            // Lấy danh sách đơn hàng cần giao (cả hôm nay và các ngày trước chưa giao)
            var donHangCanGiao = db.DonHangs
                .Include(d => d.KhachHang)
                .Include(d => d.NguoiNhans)
                .Include(d => d.ThanhToans)
                .Where(d => (d.TrangThai == "Chờ xử lý" || d.TrangThai == "Đang giao"))
                .OrderByDescending(d => d.NgayDat)
                .ToList();

            ViewBag.DonHangCanGiao = donHangCanGiao;

            // Tạo dropdown trạng thái
            ViewBag.TrangThaiList = new SelectList(new[]
            {
        new { Value = "", Text = "Tất cả trạng thái" },
        new { Value = "Chờ xử lý", Text = "Chờ xử lý" },
        new { Value = "Đang giao", Text = "Đang giao" },
        new { Value = "Giao hàng thành công", Text = "Giao hàng thành công" },
        new { Value = "Giao hàng thất bại", Text = "Giao hàng thất bại" }
    }, "Value", "Text", trangThai);

            ViewBag.SearchString = searchString;
            return View(deliveries);
        }

        // GET: Delivery/Details/5 - Chi tiết đơn hàng
        public ActionResult Details(int id)
        {
            var donHang = db.DonHangs
                .Include(d => d.KhachHang)
                .Include(d => d.NguoiNhans)
                .Include(d => d.ThanhToans)
                .Include(d => d.ChiTietDonHangs.Select(ct => ct.SanPham))
                .FirstOrDefault(d => d.MaDH == id);

            if (donHang == null)
            {
                return HttpNotFound();
            }

            return View(donHang);
        }

        // GET: Delivery/QuickUpdate - Cập nhật nhanh trạng thái
        public ActionResult QuickUpdate(int id, string trangThai)
        {
            try
            {
                var donHang = db.DonHangs.Find(id);
                if (donHang != null)
                {
                    // Kiểm tra tính hợp lệ của chuyển trạng thái
                    bool isValidTransition = false;

                    if (trangThai == "Đang giao" && donHang.TrangThai == "Chờ xử lý")
                    {
                        isValidTransition = true;
                    }
                    else if ((trangThai == "Giao hàng thành công" || trangThai == "Giao hàng thất bại")
                             && donHang.TrangThai == "Đang giao")
                    {
                        isValidTransition = true;
                    }

                    if (isValidTransition)
                    {
                        donHang.TrangThai = trangThai;
                        db.SaveChanges();
                        TempData["SuccessMessage"] = $"Đã cập nhật đơn hàng #{id} thành '{trangThai}'";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Không thể chuyển trạng thái này. Luồng trạng thái: Chờ xử lý → Đang giao → Thành công/Thất bại";
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = "Không tìm thấy đơn hàng";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi khi cập nhật: " + ex.Message;
            }

            return RedirectToAction("Index");
        }
        public ActionResult PriorityOrders()
        {
            var donHangCanGiao = db.DonHangs
                .Where(d => d.TrangThai == "Chờ xử lý" || d.TrangThai == "Đang giao")
                .OrderBy(d => d.NgayDat)
                .ToList();

            return View(donHangCanGiao);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}