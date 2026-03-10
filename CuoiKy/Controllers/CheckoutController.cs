using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using CuoiKy.Models;
using Newtonsoft.Json;
using System.Net.Mail;
using System.Net;

namespace CuoiKy.Controllers
{
    public class CheckoutController : Controller
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Checkout
        [ValidateInput(false)]
        public ActionResult Index()
        {
            System.Diagnostics.Debug.WriteLine("=== CHECKOUT PAGE ACCESSED ===");
            System.Diagnostics.Debug.WriteLine($"Session MaKH: {Session["MaKH"]}");

            // Kiểm tra đăng nhập
            if (Session["MaKH"] == null)
            {
                string returnUrl = "/Checkout";
                return RedirectToAction("Login", "User", new { returnUrl = returnUrl });
            }

            // Lấy giỏ hàng từ query string nếu có
            var cartData = Request.QueryString["cartData"];
            if (!string.IsNullOrEmpty(cartData))
            {
                try
                {
                    var cartItems = JsonConvert.DeserializeObject<List<CartItem>>(cartData);
                    Session["CartData"] = cartItems;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing cart data: {ex.Message}");
                }
            }

            // Lấy thông tin khách hàng để tính phí ship
            var customerInfo = GetCustomerInfo();
            var shippingFee = CalculateShippingFee(customerInfo.DiaChi);

            var model = new CheckoutViewModel
            {
                CartItems = GetCartFromRequest(),
                CustomerInfo = customerInfo,
                AvailableVouchers = GetAvailableVouchers(),
                ShippingFee = shippingFee,
                ShippingNote = "" // Khởi tạo ghi chú rỗng
            };

            System.Diagnostics.Debug.WriteLine($"Cart items count: {model.CartItems.Count}");
            System.Diagnostics.Debug.WriteLine($"Shipping fee: {shippingFee}");

            return View(model);
        }

        [HttpPost]
        public ActionResult PlaceOrder(OrderRequest orderRequest)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== PLACE ORDER START ===");
                System.Diagnostics.Debug.WriteLine($"OrderRequest: {JsonConvert.SerializeObject(orderRequest)}");

                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Lấy giỏ hàng
                var cart = GetCartFromRequest();
                if (cart == null || cart.Count == 0)
                {
                    return Json(new { success = false, message = "Giỏ hàng trống" });
                }

                // Tính phí ship dựa trên địa chỉ mới
                decimal shippingFee = CalculateShippingFee(orderRequest.ShippingInfo?.DiaChi);

                // Tạo CheckoutViewModel từ OrderRequest
                var model = new CheckoutViewModel
                {
                    CustomerInfo = new CustomerInfo
                    {
                        HoTen = orderRequest.ShippingInfo?.HoTen,
                        SoDienThoai = orderRequest.ShippingInfo?.SoDienThoai,
                        DiaChi = orderRequest.ShippingInfo?.DiaChi,
                        Email = orderRequest.ShippingInfo?.Email
                    },
                    ShippingNote = orderRequest.ShippingNote,
                    PaymentMethod = orderRequest.PaymentMethod,
                    ShippingFee = shippingFee,
                    VoucherDiscount = orderRequest.VoucherDiscount,
                    CartItems = cart
                };

                // Kiểm tra thông tin bắt buộc
                if (string.IsNullOrEmpty(model.CustomerInfo?.HoTen))
                {
                    return Json(new { success = false, message = "Vui lòng nhập họ tên người nhận" });
                }
                if (string.IsNullOrEmpty(model.CustomerInfo?.SoDienThoai))
                {
                    return Json(new { success = false, message = "Vui lòng nhập số điện thoại người nhận" });
                }
                if (string.IsNullOrEmpty(model.CustomerInfo?.DiaChi))
                {
                    return Json(new { success = false, message = "Vui lòng nhập địa chỉ giao hàng" });
                }

                int orderId = CreateOrder(model, cart);

                return Json(new
                {
                    success = true,
                    message = "Đặt hàng thành công!",
                    orderId = orderId,
                    paymentMethod = model.PaymentMethod
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PlaceOrder: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        public ActionResult OrderSuccess(int id)
        {
            try
            {
                var order = db.DonHangs.Find(id);
                var recipient = db.NguoiNhans.FirstOrDefault(n => n.MaDH == id);

                if (order == null)
                {
                    return HttpNotFound();
                }

                ViewBag.OrderId = id;
                ViewBag.OrderDate = order.NgayDat;
                ViewBag.Status = order.TrangThai;

                if (recipient != null)
                {
                    ViewBag.RecipientName = recipient.HoTen;
                    ViewBag.RecipientPhone = recipient.SoDienThoai;
                    ViewBag.RecipientAddress = recipient.DiaChi;
                    ViewBag.ShippingNote = recipient.GhiChu;
                }

                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OrderSuccess: {ex.Message}");
                ViewBag.OrderId = id;
                return View();
            }
        }

        [HttpPost]
        public ActionResult SaveCartToSession(string cartData)
        {
            try
            {
                if (!string.IsNullOrEmpty(cartData))
                {
                    var cartItems = JsonConvert.DeserializeObject<List<CartItem>>(cartData);
                    Session["CartData"] = cartItems;
                    return Json(new { success = true });
                }
                return Json(new { success = false });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public ActionResult UpdateCustomerAddress(CustomerAddressRequest addressRequest)
        {
            try
            {
                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var maKH = (int)Session["MaKH"];
                var khachHang = db.KhachHangs.FirstOrDefault(kh => kh.MaKH == maKH);

                if (khachHang != null)
                {
                    khachHang.HoTen = addressRequest.HoTen;
                    khachHang.Email = addressRequest.Email;
                    khachHang.SoDienThoai = addressRequest.SoDienThoai;
                    khachHang.DiaChi = addressRequest.DiaChi;

                    db.SaveChanges();

                    // Tính lại phí ship sau khi cập nhật địa chỉ
                    decimal newShippingFee = CalculateShippingFee(addressRequest.DiaChi);
                    string shippingInfo = GetShippingInfoText(addressRequest.DiaChi);

                    return Json(new
                    {
                        success = true,
                        message = "Cập nhật địa chỉ thành công",
                        shippingFee = newShippingFee,
                        shippingInfo = shippingInfo
                    });
                }

                return Json(new { success = false, message = "Không tìm thấy thông tin khách hàng" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        [HttpPost]
        public ActionResult GetShippingInfo(string address)
        {
            try
            {
                decimal shippingFee = CalculateShippingFee(address);
                string shippingInfo = GetShippingInfoText(address);

                return Json(new
                {
                    success = true,
                    shippingFee = shippingFee,
                    shippingInfo = shippingInfo
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    shippingFee = 15000,
                    shippingInfo = "Phí vận chuyển tiêu chuẩn"
                });
            }
        }

        [HttpPost]
        public ActionResult CalculateShipping(string address)
        {
            try
            {
                decimal shippingFee = CalculateShippingFee(address);
                string shippingInfo = GetShippingInfoText(address);

                return Json(new
                {
                    success = true,
                    shippingFee = shippingFee,
                    shippingInfo = shippingInfo
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    shippingFee = 15000,
                    shippingInfo = "Phí vận chuyển tiêu chuẩn"
                });
            }
        }

        public ActionResult GetOrderDetails(int id)
        {
            try
            {
                var order = db.DonHangs.Find(id);
                var recipient = db.NguoiNhans.FirstOrDefault(n => n.MaDH == id);

                if (order == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng" }, JsonRequestBehavior.AllowGet);
                }

                var result = new
                {
                    success = true,
                    orderId = order.MaDH,
                    orderDate = order.NgayDat.GetValueOrDefault().ToString("dd/MM/yyyy HH:mm"),
                    status = order.TrangThai,
                    totalAmount = order.TongTien?.ToString("N0") + " ₫",
                    recipient = recipient != null ? new
                    {
                        name = recipient.HoTen,
                        phone = recipient.SoDienThoai,
                        address = recipient.DiaChi,
                        email = recipient.Email,
                        note = recipient.GhiChu
                    } : null
                };

                return Json(result, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public ActionResult ProcessVNPayOrder(OrderRequest orderRequest)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== PROCESS VNPAY ORDER ===");
                System.Diagnostics.Debug.WriteLine($"OrderRequest: {JsonConvert.SerializeObject(orderRequest)}");

                if (Session["MaKH"] == null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Lấy giỏ hàng
                var cart = GetCartFromRequest();
                if (cart == null || cart.Count == 0)
                {
                    return Json(new { success = false, message = "Giỏ hàng trống" });
                }

                // Lưu thông tin đơn hàng vào session để sử dụng sau khi VNPAY thành công
                Session["VNPay_OrderInfo"] = new
                {
                    CartItems = cart,
                    ShippingInfo = orderRequest.ShippingInfo,
                    ShippingFee = orderRequest.ShippingFee,
                    VoucherDiscount = orderRequest.VoucherDiscount,
                    TotalAmount = orderRequest.TotalAmount,
                    ShippingNote = orderRequest.ShippingNote
                };

                System.Diagnostics.Debug.WriteLine($"✅ Đã lưu thông tin VNPAY order vào session");
                System.Diagnostics.Debug.WriteLine($"📦 Số sản phẩm: {cart.Count}");

                return Json(new { success = true, message = "Sẵn sàng chuyển hướng đến VNPAY" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ProcessVNPayOrder: {ex.Message}");
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        #region Private Methods

        private List<CartItem> GetCartFromRequest()
        {
            try
            {
                // Lấy dữ liệu giỏ hàng từ Request (sẽ được gửi từ JavaScript)
                var cartJson = Request.Form["cartData"] ?? Request.QueryString["cartData"];

                if (!string.IsNullOrEmpty(cartJson))
                {
                    System.Diagnostics.Debug.WriteLine($"Cart JSON from request: {cartJson}");

                    // Giải mã JSON
                    var cartItems = JsonConvert.DeserializeObject<List<CartItem>>(cartJson);
                    return cartItems ?? new List<CartItem>();
                }

                // Fallback: lấy từ localStorage qua session (nếu có)
                if (Session["CartData"] != null)
                {
                    return Session["CartData"] as List<CartItem> ?? new List<CartItem>();
                }

                return new List<CartItem>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting cart: {ex.Message}");
                return new List<CartItem>();
            }
        }

        private CustomerInfo GetCustomerInfo()
        {
            try
            {
                var maKH = (int)Session["MaKH"];
                var khachHang = db.KhachHangs.FirstOrDefault(kh => kh.MaKH == maKH);

                if (khachHang != null)
                {
                    return new CustomerInfo
                    {
                        MaKH = maKH,
                        HoTen = khachHang.HoTen ?? "Khách hàng",
                        Email = khachHang.Email ?? "",
                        SoDienThoai = khachHang.SoDienThoai ?? "",
                        DiaChi = khachHang.DiaChi ?? "Chưa cập nhật địa chỉ"
                    };
                }

                return new CustomerInfo
                {
                    MaKH = maKH,
                    HoTen = "Khách hàng",
                    Email = "",
                    SoDienThoai = "",
                    DiaChi = "Chưa cập nhật địa chỉ"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting customer info: {ex.Message}");
                return new CustomerInfo
                {
                    MaKH = 0,
                    HoTen = "Khách hàng",
                    Email = "",
                    SoDienThoai = "",
                    DiaChi = "Chưa cập nhật địa chỉ"
                };
            }
        }

        private List<Voucher> GetAvailableVouchers()
        {
            return new List<Voucher>
            {
                new Voucher { Code = "FREESHIP", Description = "Miễn phí vận chuyển", Discount = 15000, Type = "shipping" },
                new Voucher { Code = "EVOL10", Description = "Giảm 10% đơn hàng", Discount = 0.1m, Type = "percentage", MaxDiscount = 50000 },
                new Voucher { Code = "EVOL20", Description = "Giảm 20% đơn hàng", Discount = 0.2m, Type = "percentage", MaxDiscount = 100000 }
            };
        }

        private decimal CalculateShippingFee(string customerAddress)
        {
            try
            {
                if (string.IsNullOrEmpty(customerAddress) || customerAddress == "Chưa cập nhật địa chỉ")
                {
                    return 15000; // Mặc định 15k nếu chưa có địa chỉ
                }

                // Chuẩn hóa địa chỉ để so sánh
                string normalizedAddress = customerAddress.ToLower();

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
                    return 15000; // Phí cố định cho nội thành: 15,000 ₫ (giảm 5k)
                }

                // Kiểm tra ngoại thành TP HCM
                if (ngoaiThanhDistricts.Any(district => normalizedAddress.Contains(district)))
                {
                    return 20000; // Phí cho ngoại thành: 20,000 ₫ (giảm 5k)
                }

                // Kiểm tra các tỉnh thành khác
                if (normalizedAddress.Contains("hồ chí minh") || normalizedAddress.Contains("tphcm") ||
                    normalizedAddress.Contains("tp. hcm") || normalizedAddress.Contains("sài gòn"))
                {
                    return 15000; // Mặc định cho TP HCM
                }

                // CÁC TỈNH THÀNH KHÁC (ĐÃ GIẢM 5K)
                var tinhThanhKhac = new Dictionary<List<string>, decimal>
                {
                    // Tỉnh lân cận: 25,000 ₫ (giảm 5k)
                    { new List<string> {
                        "bình dương", "đồng nai", "long an", "tây ninh",
                        "bà rịa", "vũng tàu", "binh duong", "dong nai",
                        "long an", "tay ninh", "ba ria", "vung tau"
                    }, 25000 },
                    
                    // Đồng bằng sông Cửu Long: 30,000 ₫ (giảm 5k)
                    { new List<string> {
                        "tiền giang", "bến tre", "vĩnh long", "cần thơ",
                        "đồng tháp", "an giang", "kiên giang", "hậu giang",
                        "sóc trăng", "bạc liêu", "cà mau", "tien giang",
                        "ben tre", "vinh long", "can tho", "dong thap",
                        "an giang", "kien giang", "hau giang", "soc trang",
                        "bac lieu", "ca mau"
                    }, 30000 },
                    
                    // Miền Trung: 35,000 ₫ (giảm 5k)
                    { new List<string> {
                        "khánh hòa", "phú yên", "bình định", "quảng nam",
                        "đà nẵng", "ninh thuận", "bình thuận", "quảng ngãi",
                        "khánh hoa", "phu yen", "binh dinh", "quang nam",
                        "da nang", "ninh thuan", "binh thuan", "quang ngai"
                    }, 35000 },
                    
                    // Miền Bắc: 40,000 ₫ (giảm 5k)
                    { new List<string> {
                        "hà nội", "hải phòng", "quảng ninh", "bắc ninh",
                        "hải dương", "hưng yên", "thái bình", "nam định",
                        "ha noi", "hai phong", "quang ninh", "bac ninh",
                        "hai duong", "hung yen", "thai binh", "nam dinh"
                    }, 40000 },
                    
                    // Miền Trung xa: 45,000 ₫ (giảm 5k)
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

                // Mặc định cho các tỉnh thành khác: 35,000 ₫ (giảm 5k)
                return 35000;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating shipping fee: {ex.Message}");
                return 15000; // Mặc định nếu có lỗi
            }
        }

        private string GetShippingInfoText(string address)
        {
            if (string.IsNullOrEmpty(address) || address == "Chưa cập nhật địa chỉ")
                return "Phí vận chuyển tiêu chuẩn - 15,000 ₫";

            string normalizedAddress = address.ToLower();

            // Nội thành TP HCM
            var noiThanhDistricts = new List<string>
            {
                "quận 1", "quận 2", "quận 3", "quận 4", "quận 5",
                "quận 6", "quận 7", "quận 8", "quận 9", "quận 10",
                "quận 11", "quận 12", "quận bình thạnh", "quận gò vấp",
                "quận phú nhuận", "quận tân bình", "quận tân phú",
                "quận thủ đức", "quận bình tân"
            };

            if (noiThanhDistricts.Any(district => normalizedAddress.Contains(district)))
            {
                return "15,000 ₫";
            }

            // Ngoại thành TP HCM
            var ngoaiThanhDistricts = new List<string>
            {
                "huyện bình chánh", "huyện cần giờ", "huyện củ chi",
                "huyện hóc môn", "huyện nhà bè"
            };

            if (ngoaiThanhDistricts.Any(district => normalizedAddress.Contains(district)))
            {
                return "20,000 ₫";
            }

            // Các khu vực khác
            if (normalizedAddress.Contains("bình dương") || normalizedAddress.Contains("đồng nai") ||
                normalizedAddress.Contains("long an") || normalizedAddress.Contains("tây ninh") ||
                normalizedAddress.Contains("bà rịa") || normalizedAddress.Contains("vũng tàu"))
            {
                return "25,000 ₫";
            }

            if (normalizedAddress.Contains("tiền giang") || normalizedAddress.Contains("bến tre") ||
                normalizedAddress.Contains("cần thơ") || normalizedAddress.Contains("vĩnh long") ||
                normalizedAddress.Contains("đồng tháp") || normalizedAddress.Contains("an giang"))
            {
                return "30,000 ₫";
            }

            if (normalizedAddress.Contains("khánh hòa") || normalizedAddress.Contains("phú yên") ||
                normalizedAddress.Contains("đà nẵng") || normalizedAddress.Contains("quảng nam"))
            {
                return "30,000 ₫";
            }

            if (normalizedAddress.Contains("hà nội") || normalizedAddress.Contains("hải phòng") ||
                normalizedAddress.Contains("quảng ninh") || normalizedAddress.Contains("bắc ninh"))
            {
                return "35,000 ₫";
            }

            if (normalizedAddress.Contains("thanh hóa") || normalizedAddress.Contains("nghệ an") ||
                normalizedAddress.Contains("hà tĩnh") || normalizedAddress.Contains("quảng bình"))
            {
                return "35,000 ₫";
            }

            if (normalizedAddress.Contains("hồ chí minh") || normalizedAddress.Contains("tphcm"))
            {
                return "15,000 ₫";
            }

            return "35,000 ₫";
        }

        private int CreateOrder(CheckoutViewModel model, List<CartItem> cart)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== CREATE ORDER ===");
                System.Diagnostics.Debug.WriteLine($"Customer: {model.CustomerInfo.HoTen}, Phone: {model.CustomerInfo.SoDienThoai}");
                System.Diagnostics.Debug.WriteLine($"Address: {model.CustomerInfo.DiaChi}");
                System.Diagnostics.Debug.WriteLine($"Shipping Note: {model.ShippingNote}");
                System.Diagnostics.Debug.WriteLine($"Cart items: {cart.Count}");

                // Tính tổng tiền
                decimal subTotal = cart.Sum(item => item.Price * item.Quantity);
                decimal totalAmount = subTotal + model.ShippingFee - model.VoucherDiscount;

                // Tạo đơn hàng mới
                var donHang = new DonHang
                {
                    MaKH = (int)Session["MaKH"],
                    NgayDat = DateTime.Now,
                    TrangThai = "Chờ xác nhận",
                    PhiGiaoHang = model.ShippingFee,
                    TongTien = totalAmount
                };

                db.DonHangs.Add(donHang);
                db.SaveChanges(); // Lưu để lấy MaDH

                System.Diagnostics.Debug.WriteLine($"DonHang created with ID: {donHang.MaDH}");

                // Tạo thông tin người nhận (có ghi chú)
                var nguoiNhan = new NguoiNhan
                {
                    MaDH = donHang.MaDH,
                    HoTen = model.CustomerInfo.HoTen,
                    SoDienThoai = model.CustomerInfo.SoDienThoai,
                    DiaChi = model.CustomerInfo.DiaChi,
                    Email = model.CustomerInfo.Email ?? "",
                    GhiChu = model.ShippingNote ?? "", // Lưu ghi chú
                    CreatedDate = DateTime.Now
                };

                db.NguoiNhans.Add(nguoiNhan);

                // Tạo chi tiết đơn hàng
                foreach (var item in cart)
                {
                    var chiTiet = new ChiTietDonHang
                    {
                        MaDH = donHang.MaDH,
                        MaSP = item.Id,
                        SoLuong = item.Quantity,
                        DonGia = item.Price,
                    };
                    db.ChiTietDonHangs.Add(chiTiet);
                }

                // Tạo bản ghi thanh toán cho COD
                if (model.PaymentMethod == "cod")
                {
                    var thanhToan = new ThanhToan
                    {
                        MaDH = donHang.MaDH,
                        PhuongThuc = "COD",
                        SoTien = totalAmount,
                        TrangThai = "Chờ thanh toán",
                        NgayThanhToan = null
                    };
                    db.ThanhToans.Add(thanhToan);
                }

                db.SaveChanges();

                // GỬI EMAIL XÁC NHẬN ĐƠN HÀNG
                if (!string.IsNullOrEmpty(model.CustomerInfo.Email))
                {
                    SendOrderEmail(donHang.MaDH, model.CustomerInfo.Email, model.CustomerInfo.HoTen, totalAmount, model.ShippingNote);
                }

                System.Diagnostics.Debug.WriteLine($"NguoiNhan created for order: {donHang.MaDH}");
                System.Diagnostics.Debug.WriteLine("=== ORDER CREATED SUCCESSFULLY ===");

                return donHang.MaDH;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CreateOrder: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        // PHƯƠNG THỨC GỬI EMAIL XÁC NHẬN ĐƠN HÀNG
        private void SendOrderEmail(int orderId, string customerEmail, string customerName, decimal totalAmount, string shippingNote)
        {
            try
            {
                if (string.IsNullOrEmpty(customerEmail))
                {
                    System.Diagnostics.Debug.WriteLine("Không có email khách hàng, bỏ qua gửi email");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Đang gửi email xác nhận đơn hàng #{orderId} đến: {customerEmail}");

                // Cấu hình SMTP - Dùng thông tin từ MailController của bạn
                var smtpClient = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential("2324802010262@student.tdmu.edu.vn", "pdod zqtc rvha clkg"),
                    EnableSsl = true
                };

                // Tạo nội dung email (thêm ghi chú nếu có)
                string noteText = string.IsNullOrEmpty(shippingNote) ? "Không có ghi chú" : shippingNote;

                string subject = $"Xác nhận đơn hàng #{orderId} - EvolStore";
                string body = $@"
XIN CHÀO {customerName.ToUpper()},

Cảm ơn bạn đã đặt hàng tại EvolStore!

🎯 THÔNG TIN ĐƠN HÀNG
══════════════════════════════════════════
📋 Mã đơn hàng: #{orderId}
📅 Ngày đặt: {DateTime.Now:dd/MM/yyyy HH:mm}
💰 Tổng tiền: {totalAmount:N0} ₫
📝 Ghi chú: {noteText}
📦 Trạng thái: Đã tiếp nhận
══════════════════════════════════════════

Đơn hàng của bạn sẽ được xử lý trong thời gian sớm nhất.
Bạn sẽ nhận được thông báo khi đơn hàng được giao.

📞 Liên hệ hỗ trợ: 1900 1234
🌐 Website: https://evolstore.com

Trân trọng,
Đội ngũ EvolStore 🛍️
";

                // Tạo và gửi email
                var mailMessage = new MailMessage();
                mailMessage.From = new MailAddress("2324802010262@student.tdmu.edu.vn", "EvolStore");
                mailMessage.To.Add(customerEmail);
                mailMessage.Subject = subject;
                mailMessage.Body = body;
                mailMessage.IsBodyHtml = false;

                smtpClient.Send(mailMessage);

                System.Diagnostics.Debug.WriteLine($"✅ Đã gửi email xác nhận đơn hàng #{orderId} đến: {customerEmail}");
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nhưng không làm hỏng đơn hàng
                System.Diagnostics.Debug.WriteLine($"❌ Lỗi gửi email: {ex.Message}");
            }
        }

        #endregion
    }

    // Các class hỗ trợ
    public class OrderRequest
    {
        public string PaymentMethod { get; set; }
        public string VoucherCode { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal VoucherDiscount { get; set; }
        public decimal TotalAmount { get; set; }
        public ShippingInfo ShippingInfo { get; set; }
        public string ShippingNote { get; set; }
        public List<CartItem> CartItems { get; set; }
    }

    public class ShippingInfo
    {
        public string HoTen { get; set; }
        public string SoDienThoai { get; set; }
        public string DiaChi { get; set; }
        public string Email { get; set; }
        public string GhiChu { get; set; }
    }

    public class CustomerAddressRequest
    {
        public string HoTen { get; set; }
        public string Email { get; set; }
        public string SoDienThoai { get; set; }
        public string DiaChi { get; set; }
    }

    public class CheckoutViewModel
    {
        public List<CartItem> CartItems { get; set; }
        public CustomerInfo CustomerInfo { get; set; }
        public List<Voucher> AvailableVouchers { get; set; }
        public string ShippingNote { get; set; }
        public string SelectedVoucher { get; set; }
        public decimal VoucherDiscount { get; set; }
        public string PaymentMethod { get; set; }
        public decimal ShippingFee { get; set; }

        public decimal SubTotal => CartItems?.Sum(item => item.Price * item.Quantity) ?? 0;
        public decimal TotalAmount => SubTotal + ShippingFee - VoucherDiscount;
    }

    public class CartItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string Size { get; set; }
        public string Color { get; set; }
        public string Image { get; set; }
        public decimal Total => Price * Quantity;
    }

    public class CustomerInfo
    {
        public int MaKH { get; set; }
        public string HoTen { get; set; }
        public string Email { get; set; }
        public string SoDienThoai { get; set; }
        public string DiaChi { get; set; }
    }

    public class Voucher
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal Discount { get; set; }
        public string Type { get; set; }
        public decimal MaxDiscount { get; set; }
    }
}