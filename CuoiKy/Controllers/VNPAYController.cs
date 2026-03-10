using CuoiKy.Models;
using CuoiKy.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace CuoiKy.Controllers
{
    public class VNPAYController : Controller
    {
        // GET: Thanh toán
        [HttpPost]
        public JsonResult CreatePayment(decimal amount, string orderDescription, string customerName,
                               string customerEmail, string customerPhone, string customerAddress,
                               string customerNote = "") // THÊM THAM SỐ GHI CHÚ
        {
            try
            {
                // Lấy config từ Web.config
                var vnp_Url = System.Configuration.ConfigurationManager.AppSettings["vnp_Url"];
                var vnp_TmnCode = System.Configuration.ConfigurationManager.AppSettings["vnp_TmnCode"];
                var vnp_HashSecret = System.Configuration.ConfigurationManager.AppSettings["vnp_HashSecret"];

                // QUAN TRỌNG: Ghi đè ReturnUrl thành localhost của bạn
                var vnp_ReturnUrl = "https://localhost:44318/VNPay/PaymentReturn";

                Console.WriteLine($"🔗 ReturnUrl: {vnp_ReturnUrl}");

                // Tạo orderId
                string orderId = "EVOL_" + DateTime.Now.ToString("yyyyMMddHHmmss");

                // Build URL thanh toán VNPay
                var vnpay = new VnPayLibrary();

                vnpay.AddRequestData("vnp_Version", "2.1.0");
                vnpay.AddRequestData("vnp_Command", "pay");
                vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
                vnpay.AddRequestData("vnp_Amount", (amount * 100).ToString());
                vnpay.AddRequestData("vnp_BankCode", "");
                vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", GetIpAddress());
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", orderDescription);
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", vnp_ReturnUrl);
                vnpay.AddRequestData("vnp_TxnRef", orderId);
                vnpay.AddRequestData("vnp_ExpireDate", DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss"));

                string paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);

                // Lưu session - THÊM GHI CHÚ
                Session["VNPay_Order"] = new
                {
                    OrderId = orderId,
                    Amount = amount,
                    Description = orderDescription,
                    CustomerName = customerName,
                    CustomerEmail = customerEmail,
                    CustomerPhone = customerPhone,
                    CustomerAddress = customerAddress,
                    CustomerNote = customerNote, // THÊM GHI CHÚ
                    CreatedDate = DateTime.Now
                };

                System.Diagnostics.Debug.WriteLine($"✅ Đã lưu VNPay order vào session với ghi chú: {customerNote}");

                return Json(new
                {
                    success = true,
                    paymentUrl = paymentUrl,
                    orderId = orderId
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Lỗi: {ex.Message}"
                });
            }
        }

        // Action xử lý kết quả trả về từ VNPay
        public ActionResult PaymentReturn()
        {
            try
            {
                if (Request.QueryString.Count > 0)
                {
                    var vnp_HashSecret = System.Configuration.ConfigurationManager.AppSettings["vnp_HashSecret"];
                    var vnpay = new VnPayLibrary();

                    // Lấy dữ liệu trả về từ VNPay
                    foreach (string s in Request.QueryString)
                    {
                        if (!string.IsNullOrEmpty(s) && s.StartsWith("vnp_"))
                        {
                            vnpay.AddResponseData(s, Request.QueryString[s]);
                        }
                    }

                    var orderId = vnpay.GetResponseData("vnp_TxnRef");
                    var vnp_Amount = Convert.ToInt64(vnpay.GetResponseData("vnp_Amount")) / 100;
                    var vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
                    var vnp_TransactionNo = vnpay.GetResponseData("vnp_TransactionNo");
                    var vnp_BankCode = vnpay.GetResponseData("vnp_BankCode");
                    var vnp_OrderInfo = vnpay.GetResponseData("vnp_OrderInfo");
                    var vnp_PayDate = vnpay.GetResponseData("vnp_PayDate");
                    var vnp_SecureHash = Request.QueryString["vnp_SecureHash"];

                    bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, vnp_HashSecret);

                    if (checkSignature)
                    {
                        // Lấy thông tin đơn hàng từ session
                        var vnpayOrder = Session["VNPay_Order"] as dynamic;

                        if (vnp_ResponseCode == "00")
                        {
                            // THANH TOÁN THÀNH CÔNG
                            ViewBag.Success = true;
                            ViewBag.Message = "Thanh toán thành công qua VNPay";
                            ViewBag.OrderId = orderId;
                            ViewBag.Amount = vnp_Amount;
                            ViewBag.TransactionNo = vnp_TransactionNo;
                            ViewBag.BankCode = vnp_BankCode;
                            ViewBag.PayDate = vnp_PayDate;

                            // TODO: Gọi CheckoutController để tạo đơn hàng
                            CreateOrderAfterVNPaySuccess(vnpayOrder, vnp_TransactionNo);

                            // Xóa session
                            Session.Remove("VNPay_Order");

                            return View();
                        }
                        else
                        {
                            // THANH TOÁN THẤT BẠI
                            ViewBag.Success = false;
                            ViewBag.Message = $"Thanh toán thất bại. Mã lỗi: {vnp_ResponseCode}";
                            ViewBag.OrderId = orderId;

                            return View();
                        }
                    }
                    else
                    {
                        ViewBag.Success = false;
                        ViewBag.Message = "Chữ ký không hợp lệ";
                        return View();
                    }
                }
                else
                {
                    ViewBag.Success = false;
                    ViewBag.Message = "Không có dữ liệu trả về từ VNPay";
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Success = false;
                ViewBag.Message = "Lỗi xử lý: " + ex.Message;
                return View();
            }
        }

        private void CreateOrderAfterVNPaySuccess(dynamic vnpayOrder, string transactionNo)
        {
            try
            {
                using (var db = new EvolStoreEntities2())
                {
                    // Lấy giỏ hàng từ session
                    var cartItems = Session["CartData"] as List<CartItem> ?? new List<CartItem>();

                    // Debug thông tin
                    System.Diagnostics.Debug.WriteLine($"=== CREATE ORDER FROM VNPAY ===");
                    System.Diagnostics.Debug.WriteLine($"Cart items count: {cartItems?.Count ?? 0}");
                    System.Diagnostics.Debug.WriteLine($"Customer: {vnpayOrder?.CustomerName}");
                    System.Diagnostics.Debug.WriteLine($"Transaction: {transactionNo}");

                    // DEBUG: Kiểm tra ghi chú
                    string ghiChu = vnpayOrder?.CustomerNote ?? "";
                    System.Diagnostics.Debug.WriteLine($"📝 Ghi chú từ VNPay: {ghiChu}");

                    if (cartItems == null || cartItems.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Giỏ hàng trống, không thể tạo đơn hàng");
                        return;
                    }

                    // Tính tổng tiền và phí ship
                    decimal subTotal = cartItems.Sum(item => item.Price * item.Quantity);
                    decimal shippingFee = CalculateShippingFee(vnpayOrder.CustomerAddress);
                    decimal totalAmount = subTotal + shippingFee;

                    System.Diagnostics.Debug.WriteLine($"SubTotal: {subTotal}, Shipping: {shippingFee}, Total: {totalAmount}");

                    // Tạo đơn hàng mới
                    var donHang = new DonHang
                    {
                        MaKH = Session["MaKH"] != null ? (int)Session["MaKH"] : 0,
                        NgayDat = DateTime.Now,
                        TrangThai = "Chờ xác nhận",
                        PhiGiaoHang = shippingFee,
                        TongTien = totalAmount
                    };

                    db.DonHangs.Add(donHang);
                    db.SaveChanges();

                    System.Diagnostics.Debug.WriteLine($"✅ Đơn hàng được tạo với ID: {donHang.MaDH}");

                    // Tạo thông tin người nhận
                    var nguoiNhan = new NguoiNhan
                    {
                        MaDH = donHang.MaDH,
                        HoTen = vnpayOrder.CustomerName,
                        SoDienThoai = vnpayOrder.CustomerPhone,
                        DiaChi = vnpayOrder.CustomerAddress,
                        Email = vnpayOrder.CustomerEmail ?? "",
                        GhiChu = ghiChu,
                        CreatedDate = DateTime.Now
                    };

                    db.NguoiNhans.Add(nguoiNhan);

                    // Tạo chi tiết đơn hàng
                    foreach (var item in cartItems)
                    {
                        var chiTiet = new ChiTietDonHang
                        {
                            MaDH = donHang.MaDH,
                            MaSP = item.Id,
                            SoLuong = item.Quantity,
                            DonGia = item.Price,
                        };
                        db.ChiTietDonHangs.Add(chiTiet);
                        System.Diagnostics.Debug.WriteLine($"📦 Thêm sản phẩm: {item.Name} x {item.Quantity}");
                    }

                    // THÊM: Tạo bản ghi thanh toán cho VNPay
                    var thanhToan = new ThanhToan
                    {
                        MaDH = donHang.MaDH,
                        PhuongThuc = "VNPay",
                        SoTien = totalAmount,
                        TrangThai = "Đã thanh toán",
                        NgayThanhToan = DateTime.Now,
                        // Nếu có mã giao dịch, bạn có thể lưu vào trường khác nếu có
                    };
                    db.ThanhToans.Add(thanhToan);

                    db.SaveChanges();

                    System.Diagnostics.Debug.WriteLine($"✅ Đơn hàng {donHang.MaDH} đã được tạo thành công qua VNPAY!");
                    System.Diagnostics.Debug.WriteLine($"💰 Số tiền: {totalAmount:N0} ₫");
                    System.Diagnostics.Debug.WriteLine($"📦 Số sản phẩm: {cartItems.Count}");
                    System.Diagnostics.Debug.WriteLine($"📝 Ghi chú đã lưu: {ghiChu}");
                    System.Diagnostics.Debug.WriteLine($"💳 Đã tạo bản ghi thanh toán VNPay");

                    // Xóa session sau khi tạo đơn hàng thành công
                    Session.Remove("CartData");
                    Session.Remove("VNPay_Order");

                    System.Diagnostics.Debug.WriteLine("✅ Đã xóa session giỏ hàng");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Lỗi khi tạo đơn hàng VNPAY: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
            }
        }

        // THÊM HÀM TÍNH PHÍ SHIP TRẢ VỀ SỐ (giống trong CheckoutController)
        private decimal CalculateShippingFee(string address)
        {
            try
            {
                if (string.IsNullOrEmpty(address) || address == "Chưa cập nhật địa chỉ")
                {
                    return 15000;
                }

                string normalizedAddress = address.ToLower();

                // DANH SÁCH QUẬN/HUYỆN NỘI THÀNH TP HCM
                var noiThanhDistricts = new List<string>
        {
            "quận 1", "quận 2", "quận 3", "quận 4", "quận 5",
            "quận 6", "quận 7", "quận 8", "quận 9", "quận 10",
            "quận 11", "quận 12", "quận bình thạnh", "quận gò vấp",
            "quận phú nhuận", "quận tân bình", "quận tân phú",
            "quận thủ đức", "quận bình tân"
        };

                // DANH SÁCH HUYỆN NGOẠI THÀNH TP HCM
                var ngoaiThanhDistricts = new List<string>
        {
            "huyện bình chánh", "huyện cần giờ", "huyện củ chi",
            "huyện hóc môn", "huyện nhà bè"
        };

                // Kiểm tra nội thành TP HCM
                if (noiThanhDistricts.Any(district => normalizedAddress.Contains(district)))
                {
                    return 15000;
                }

                // Kiểm tra ngoại thành TP HCM
                if (ngoaiThanhDistricts.Any(district => normalizedAddress.Contains(district)))
                {
                    return 20000;
                }

                // Kiểm tra các tỉnh thành khác
                if (normalizedAddress.Contains("hồ chí minh") || normalizedAddress.Contains("tphcm") ||
                    normalizedAddress.Contains("tp. hcm") || normalizedAddress.Contains("sài gòn"))
                {
                    return 15000;
                }

                // CÁC TỈNH THÀNH KHÁC
                var tinhThanhKhac = new Dictionary<List<string>, decimal>
        {
            { new List<string> {
                "bình dương", "đồng nai", "long an", "tây ninh",
                "bà rịa", "vũng tàu", "binh duong", "dong nai",
                "long an", "tay ninh", "ba ria", "vung tau"
            }, 25000 },

            { new List<string> {
                "tiền giang", "bến tre", "vĩnh long", "cần thơ",
                "đồng tháp", "an giang", "kiên giang", "hậu giang",
                "sóc trăng", "bạc liêu", "cà mau", "tien giang",
                "ben tre", "vinh long", "can tho", "dong thap",
                "an giang", "kien giang", "hau giang", "soc trang",
                "bac lieu", "ca mau"
            }, 30000 },

            { new List<string> {
                "khánh hòa", "phú yên", "bình định", "quảng nam",
                "đà nẵng", "ninh thuận", "bình thuận", "quảng ngãi",
                "khánh hoa", "phu yen", "binh dinh", "quang nam",
                "da nang", "ninh thuan", "binh thuan", "quang ngai"
            }, 35000 },

            { new List<string> {
                "hà nội", "hải phòng", "quảng ninh", "bắc ninh",
                "hải dương", "hưng yên", "thái bình", "nam định",
                "ha noi", "hai phong", "quang ninh", "bac ninh",
                "hai duong", "hung yen", "thai binh", "nam dinh"
            }, 40000 },

            { new List<string> {
                "thanh hóa", "nghệ an", "hà tĩnh", "quảng bình",
                "quảng trị", "thừa thiên huế", "thanh hoa", "nghe an",
                "ha tinh", "quang binh", "quang tri", "thua thien hue"
            }, 45000 }
        };

                foreach (var kvp in tinhThanhKhac)
                {
                    if (kvp.Key.Any(tinh => normalizedAddress.Contains(tinh)))
                    {
                        return kvp.Value;
                    }
                }

                return 35000;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating shipping fee: {ex.Message}");
                return 15000;
            }
        }
        // Hàm lấy IP address
        private string GetIpAddress()
        {
            string ipAddress;
            try
            {
                ipAddress = Request.ServerVariables["HTTP_X_FORWARDED_FOR"];

                if (string.IsNullOrEmpty(ipAddress) || (ipAddress.ToLower() == "unknown"))
                    ipAddress = Request.ServerVariables["REMOTE_ADDR"];
            }
            catch (Exception ex)
            {
                ipAddress = "Invalid IP:" + ex.Message;
            }

            return ipAddress;
        }
    }
}