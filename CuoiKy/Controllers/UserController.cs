using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using CuoiKy.Models;
using CuoiKy.Utils;
using Microsoft.Owin.Security;
using System.Security.Claims;

namespace CuoiKy.Controllers
{
    public class UserController : Controller
    {
        private EvolStoreEntities2 db = new EvolStoreEntities2();

        // GET: Đăng nhập
        public ActionResult Login(string returnUrl)
        {
            // Nếu đã đăng nhập, chuyển hướng
            if (Session["MaKH"] != null)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                else
                    return RedirectToAction("Index", "EvolStore");
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST: Đăng nhập thường
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(FormCollection form, string returnUrl)
        {
            string tenDangNhap = form["TenDangNhap"];
            string matKhau = form["MatKhau"];
            bool rememberMe = form["RememberMe"] == "on" || form["RememberMe"] == "true";

            // Ưu tiên returnUrl từ form, nếu không có thì từ parameter
            returnUrl = form["returnUrl"] ?? returnUrl;

            Debug.WriteLine($"Login attempt: {tenDangNhap}, ReturnUrl: {returnUrl}");

            // Xác minh reCAPTCHA
            var captchaResponse = Request["g-recaptcha-response"];
            if (!SecurityHelper.VerifyReCaptcha(captchaResponse, Request.UserHostAddress))
            {
                ViewBag.Error = "Vui lòng xác nhận reCAPTCHA.";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // Tìm tài khoản
            var taiKhoan = db.TaiKhoans.FirstOrDefault(tk => tk.TenDangNhap == tenDangNhap);

            // Xác minh mật khẩu - BCrypt hash (hoặc backward compatibility cho mật khẩu cũ dạng text)
            bool passwordValid = false;
            if (taiKhoan != null && !string.IsNullOrEmpty(taiKhoan.MatKhau))
            {
                if (SecurityHelper.IsBcryptHash(taiKhoan.MatKhau))
                    passwordValid = SecurityHelper.VerifyPassword(matKhau, taiKhoan.MatKhau);
                else
                {
                    // Backward: mật khẩu cũ lưu dạng text - sau khi đăng nhập thành công sẽ nâng cấp lên BCrypt
                    if (taiKhoan.MatKhau == matKhau)
                    {
                        passwordValid = true;
                        try { taiKhoan.MatKhau = SecurityHelper.HashPassword(matKhau); db.SaveChanges(); } catch { }
                    }
                }
            }

            if (taiKhoan != null && passwordValid && (taiKhoan.TrangThai ?? true))
            {
                if (taiKhoan.VaiTro == "KhachHang" && taiKhoan.MaKH.HasValue)
                {
                    var khachHang = db.KhachHangs.Find(taiKhoan.MaKH.Value);

                    // Lưu Session
                    Session["MaTK"] = taiKhoan.MaTK;
                    Session["TenDangNhap"] = taiKhoan.TenDangNhap;
                    Session["VaiTro"] = taiKhoan.VaiTro;
                    Session["MaKH"] = taiKhoan.MaKH;
                    Session["TenKhachHang"] = khachHang?.HoTen ?? taiKhoan.TenDangNhap;
                    Session["Email"] = khachHang?.Email;
                    Session["SoDienThoai"] = khachHang?.SoDienThoai;
                    Session["DiaChi"] = khachHang?.DiaChi;

                    // Forms Authentication
                    FormsAuthentication.SetAuthCookie(taiKhoan.TenDangNhap, rememberMe);

                    Debug.WriteLine($"Login successful for: {tenDangNhap}");

                    // Redirect
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);
                    else
                        return RedirectToAction("Index", "EvolStore");
                }
                else
                {
                    ViewBag.Error = "Tài khoản không có quyền truy cập!";
                }
            }
            else
            {
                ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng!";
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // ==================== GOOGLE LOGIN - ĐƠN GIẢN NHẤT ====================

        // POST: Google Login (không cần AntiForgeryToken)
        [HttpPost]
        public ActionResult GoogleLogin(string returnUrl)
        {
            Debug.WriteLine("GoogleLogin POST called");

            // Đăng ký Redirect URI trong Google Console phải là:
            // https://localhost:44318/User/GoogleCallback

            HttpContext.GetOwinContext().Authentication.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("GoogleCallback", new { returnUrl = returnUrl })
                },
                "Google");

            return new HttpUnauthorizedResult();
        }

        // GET: Google Callback
        [AllowAnonymous]
        public ActionResult GoogleCallback(string returnUrl)
        {
            try
            {
                Debug.WriteLine("=== GOOGLE CALLBACK ===");

                // Kiểm tra authentication result
                var authenticateResult = HttpContext.GetOwinContext()
                    .Authentication.AuthenticateAsync("ExternalCookie").Result;

                if (authenticateResult == null)
                {
                    TempData["Error"] = "Không thể xác thực với Google.";
                    return RedirectToAction("Login");
                }

                // Lấy email từ claims
                string email = null;
                string name = null;

                // Tìm email trong claims
                var emailClaim = authenticateResult.Identity.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.Email ||
                                        c.Type.EndsWith("emailaddress") ||
                                        c.Type == "email");

                email = emailClaim?.Value;

                // Tìm tên
                var nameClaim = authenticateResult.Identity.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.Name ||
                                        c.Type == "name");

                name = nameClaim?.Value ?? (email?.Split('@')[0] ?? "User");

                if (string.IsNullOrEmpty(email))
                {
                    TempData["Error"] = "Không nhận được email từ Google.";
                    return RedirectToAction("Login");
                }

                Debug.WriteLine($"Google login: {email}, Name: {name}");

                // Xóa authentication tạm
                HttpContext.GetOwinContext().Authentication.SignOut("ExternalCookie");

                // Tìm tài khoản
                var taiKhoan = db.TaiKhoans.FirstOrDefault(tk => tk.TenDangNhap == email);

                if (taiKhoan == null)
                {
                    // Tạo tài khoản mới
                    return RegisterFromGoogle(email, name, returnUrl);
                }
                else
                {
                    // Đăng nhập tài khoản đã có
                    return LoginFromGoogle(taiKhoan, returnUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Google Callback Error: {ex.Message}");
                TempData["Error"] = "Lỗi đăng nhập Google: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        // Tạo tài khoản từ Google
        private ActionResult RegisterFromGoogle(string email, string name, string returnUrl)
        {
            try
            {
                // Tạo khách hàng mới
                var khachHang = new KhachHang
                {
                    HoTen = name,
                    Email = email,
                    SoDienThoai = "",
                    DiaChi = "",
                    
                };

                db.KhachHangs.Add(khachHang);
                db.SaveChanges();

                // Tạo tài khoản mới - OAuth không dùng mật khẩu, dùng BCrypt hash ngẫu nhiên
                var randomPass = "google_" + Guid.NewGuid().ToString("N").Substring(0, 16);
                var taiKhoan = new TaiKhoan
                {
                    TenDangNhap = email,
                    MatKhau = SecurityHelper.HashPassword(randomPass),
                    VaiTro = "KhachHang",
                    TrangThai = true,
                    NgayTao = DateTime.Now,
                    MaKH = khachHang.MaKH
                };

                db.TaiKhoans.Add(taiKhoan);
                db.SaveChanges();

                Debug.WriteLine($"Created new account from Google: {email}");

                // Đăng nhập
                return LoginFromGoogle(taiKhoan, returnUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating Google account: {ex.Message}");
                TempData["Error"] = "Lỗi tạo tài khoản từ Google: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        // Đăng nhập từ Google
        private ActionResult LoginFromGoogle(TaiKhoan taiKhoan, string returnUrl)
        {
            // Kiểm tra trạng thái tài khoản
            if (!(taiKhoan.TrangThai ?? true))
            {
                TempData["Error"] = "Tài khoản của bạn đã bị khóa!";
                return RedirectToAction("Login");
            }

            // Lấy thông tin khách hàng
            var khachHang = db.KhachHangs.Find(taiKhoan.MaKH);

            // Cập nhật email nếu chưa có
            if (khachHang != null && string.IsNullOrEmpty(khachHang.Email))
            {
                khachHang.Email = taiKhoan.TenDangNhap;
                db.SaveChanges();
            }

            // Set session
            Session["MaTK"] = taiKhoan.MaTK;
            Session["TenDangNhap"] = taiKhoan.TenDangNhap;
            Session["VaiTro"] = taiKhoan.VaiTro;
            Session["MaKH"] = taiKhoan.MaKH;
            Session["TenKhachHang"] = khachHang?.HoTen ?? taiKhoan.TenDangNhap;
            Session["Email"] = khachHang?.Email ?? taiKhoan.TenDangNhap;
            Session["SoDienThoai"] = khachHang?.SoDienThoai;
            Session["DiaChi"] = khachHang?.DiaChi;

            // Forms authentication
            FormsAuthentication.SetAuthCookie(taiKhoan.TenDangNhap, false);

            Debug.WriteLine($"Google login successful: {taiKhoan.TenDangNhap}");

            // Redirect
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            else
                return RedirectToAction("Index", "EvolStore");
        }

        // GET: Đăng ký
        public ActionResult Register()
        {
            return View();
        }

        // POST: Đăng ký
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(FormCollection form)
        {
            string tenDangNhap = form["TenDangNhap"];
            string matKhau = form["MatKhau"];
            string confirmPassword = form["ConfirmPassword"];
            string hoTen = form["HoTen"];
            string email = form["Email"];
            string dienThoai = form["DienThoai"];
            string diaChi = form["DiaChi"];

            // Xác minh reCAPTCHA
            var captchaResponse = Request["g-recaptcha-response"];
            if (!SecurityHelper.VerifyReCaptcha(captchaResponse, Request.UserHostAddress))
            {
                ViewBag.Error = "Vui lòng xác nhận reCAPTCHA.";
                return View();
            }

            // Kiểm tra độ mạnh mật khẩu: ít nhất 8 ký tự, có 1 chữ thường và 1 chữ số
            if (string.IsNullOrWhiteSpace(matKhau) || matKhau.Length < 8)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 8 ký tự.";
                return View();
            }
            var passwordPattern = @"^(?=.*[a-z])(?=.*\d).{8,}$";
            if (!Regex.IsMatch(matKhau, passwordPattern))
            {
                ViewBag.Error = "Mật khẩu phải chứa ít nhất 1 chữ số và 1 chữ thường.";
                return View();
            }

            // Kiểm tra mật khẩu
            if (matKhau != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp!";
                return View();
            }

            // Kiểm tra tên đăng nhập tồn tại
            if (db.TaiKhoans.Any(tk => tk.TenDangNhap == tenDangNhap))
            {
                ViewBag.Error = "Tên đăng nhập đã tồn tại!";
                return View();
            }

            try
            {
                // Tạo khách hàng mới
                var khachHang = new KhachHang
                {
                    HoTen = hoTen,
                    Email = email,
                    SoDienThoai = dienThoai,
                    DiaChi = diaChi,
                    
                };

                db.KhachHangs.Add(khachHang);
                db.SaveChanges();

                // Tạo tài khoản - BCrypt hash, không lưu password dạng text
                var taiKhoan = new TaiKhoan
                {
                    TenDangNhap = tenDangNhap,
                    MatKhau = SecurityHelper.HashPassword(matKhau),
                    VaiTro = "KhachHang",
                    TrangThai = true,
                    NgayTao = DateTime.Now,
                    MaKH = khachHang.MaKH
                };

                db.TaiKhoans.Add(taiKhoan);
                db.SaveChanges();

                Debug.WriteLine($"Registered new user: {tenDangNhap}");

                // Đăng nhập ngay sau khi đăng ký
                Session["MaTK"] = taiKhoan.MaTK;
                Session["TenDangNhap"] = taiKhoan.TenDangNhap;
                Session["VaiTro"] = taiKhoan.VaiTro;
                Session["MaKH"] = taiKhoan.MaKH;
                Session["TenKhachHang"] = khachHang.HoTen;
                Session["Email"] = khachHang.Email;
                Session["SoDienThoai"] = khachHang.SoDienThoai;
                Session["DiaChi"] = khachHang.DiaChi;

                FormsAuthentication.SetAuthCookie(tenDangNhap, false);

                return RedirectToAction("Index", "EvolStore");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Registration error: {ex.Message}");
                ViewBag.Error = "Đã có lỗi xảy ra khi đăng ký: " + ex.Message;
                return View();
            }
        }

        // GET: Đăng xuất
        [HttpGet]
        public ActionResult LogoutGet()
        {
            // Clear Session
            Session.Clear();
            Session.Abandon();

            // Forms Authentication logout
            FormsAuthentication.SignOut();

            // OWIN logout (nếu có)
            if (Request.IsAuthenticated)
            {
                HttpContext.GetOwinContext().Authentication.SignOut();
            }

            Debug.WriteLine("User logged out");
            TempData["InfoMessage"] = "Bạn đã đăng xuất thành công.";
            return RedirectToAction("Index", "EvolStore");
        }

        // Kiểm tra trạng thái đăng nhập (cho AJAX)
        public JsonResult CheckLoginStatus()
        {
            bool isLoggedIn = Session["MaKH"] != null;
            string username = Session["TenKhachHang"]?.ToString() ?? "";

            return Json(new
            {
                isLoggedIn = isLoggedIn,
                username = username
            }, JsonRequestBehavior.AllowGet);
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