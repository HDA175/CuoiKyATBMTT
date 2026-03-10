using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CuoiKy.Models
{
	public class GioHang
	{
        public int MaSP { get; set; }
        public string TenSP { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
        public string Size { get; set; }
        public string MauSac { get; set; }
        public string HinhAnh { get; set; }
        public bool CoTheMua { get; set; }

        public decimal ThanhTien => Gia * SoLuong;
    }
}