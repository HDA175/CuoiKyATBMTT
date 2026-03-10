using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CuoiKy.Models
{
    public partial class SanPham
    {
        // Thuộc tính bổ sung không có trong database
        public decimal GiaGoc
        {
            get
            {
                // Tính giá gốc từ giá hiện tại và giảm giá
                var giamGiaHienTai = LayGiamGiaHienTai();
                if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                {
                    // Công thức: Giá hiện tại = Giá gốc * (1 - phần trăm giảm)
                    // => Giá gốc = Giá hiện tại / (1 - phần trăm giảm)
                    return this.Gia / (1 - giamGiaHienTai.PhanTramGiam);
                }
                return this.Gia;
            }
            set
            {
                // Không cần setter vì đây là thuộc tính tính toán
            }
        }

        // Property để tính giá sau giảm
        public decimal GiaSauGiamGia
        {
            get
            {
                var giamGiaHienTai = LayGiamGiaHienTai();
                if (giamGiaHienTai != null && giamGiaHienTai.PhanTramGiam > 0)
                {
                    return this.GiaGoc * (1 - giamGiaHienTai.PhanTramGiam);
                }
                return this.Gia;
            }
        }

        // Phương thức lấy giảm giá hiện tại
        public GiamGia LayGiamGiaHienTai()
        {
            try
            {
                var now = DateTime.Now;

                // Kiểm tra nếu GiamGias không null
                if (this.GiamGias == null)
                {
                    return null;
                }

                return this.GiamGias
                    .Where(g => g.NgayBatDau <= now &&
                               (g.NgayKetThuc == null || g.NgayKetThuc >= now))
                    .OrderByDescending(g => g.PhanTramGiam)
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Phương thức kiểm tra có đang giảm giá không
        public bool DangGiamGia()
        {
            var giamGia = LayGiamGiaHienTai();
            return giamGia != null && giamGia.PhanTramGiam > 0;
        }

        // Phương thức lấy phần trăm giảm giá hiện tại
        public decimal PhanTramGiamGiaHienTai()
        {
            var giamGia = LayGiamGiaHienTai();
            return giamGia != null ? giamGia.PhanTramGiam * 100 : 0;
        }

        // Phương thức áp dụng giảm giá và cập nhật giá
        public void ApDungGiamGia(decimal phanTramGiam, DateTime? ngayKetThuc = null)
        {
            if (phanTramGiam < 0 || phanTramGiam > 100)
                throw new ArgumentException("Phần trăm giảm giá phải từ 0 đến 100");

            var today = DateTime.Now.Date;
            decimal rate = phanTramGiam / 100m;

            // Tính giá mới
            decimal giaMoi = this.GiaGoc * (1 - rate);
            this.Gia = giaMoi;

            // Tạo giảm giá mới (giả sử có context database)
            // Việc lưu vào database sẽ được xử lý trong controller
        }

        // Phương thức xóa giảm giá
        public void XoaGiamGia()
        {
            // Cập nhật giá về giá gốc
            this.Gia = this.GiaGoc;

            // Kết thúc giảm giá (việc cập nhật database sẽ ở controller)
        }

        // Phương thức tính giá từ giá gốc và phần trăm giảm
        public decimal TinhGiaSauGiam(decimal giaGoc, decimal phanTramGiam)
        {
            if (phanTramGiam < 0 || phanTramGiam > 100)
                throw new ArgumentException("Phần trăm giảm giá phải từ 0 đến 100");

            return giaGoc * (1 - (phanTramGiam / 100m));
        }
    }
}