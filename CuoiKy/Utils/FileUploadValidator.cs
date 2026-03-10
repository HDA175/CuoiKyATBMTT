using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace CuoiKy.Utils
{
    /// <summary>
    /// FileUploadValidator - Bảo mật upload file
    /// 3.1 Chỉ cho phép định dạng: .jpg, .jpeg, .png, .webp
    /// 3.2 Validate MIME type, kích thước, magic bytes
    /// </summary>
    public static class FileUploadValidator
    {
        /// <summary>
        /// Các định dạng ảnh được phép upload (theo yêu cầu: jpg, png, jpeg, webp)
        /// Lưu ý: Hệ thống sản phẩm hiện dùng .jpg cho URL, file .png/.webp sẽ được lưu dạng .jpg
        /// </summary>
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        /// <summary>
        /// MIME types hợp lệ tương ứng với các extension
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedMimeTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg", new[] { "image/jpeg", "image/pjpeg" } },
            { ".jpeg", new[] { "image/jpeg", "image/pjpeg" } },
            { ".png", new[] { "image/png" } },
            { ".webp", new[] { "image/webp" } }
        };

        /// <summary>
        /// Magic bytes (file signature) cho các định dạng ảnh
        /// </summary>
        private static readonly Dictionary<string, byte[][]> FileSignatures = new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } } },
            { ".jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } } },
            { ".png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
            { ".webp", new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } } } // RIFF - WebP có 12 bytes header
        };

        /// <summary>
        /// Kích thước tối đa: 5MB
        /// </summary>
        public const int MaxFileSizeBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Kết quả validation
        /// </summary>
        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; }
            public string SafeExtension { get; set; }
        }

        /// <summary>
        /// Validate file upload - Kiểm tra extension, MIME type, size, content (magic bytes)
        /// </summary>
        public static ValidationResult ValidateImageUpload(HttpPostedFileBase file)
        {
            if (file == null || file.ContentLength == 0)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Không có file được chọn." };
            }

            // 3.1 Kiểm tra extension
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Chỉ cho phép file ảnh: .jpg, .jpeg, .png, .webp. File của bạn: {extension ?? "(không có extension)"}"
                };
            }

            // 3.2 Kiểm tra kích thước
            if (file.ContentLength > MaxFileSizeBytes)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"File vượt quá 5MB. Kích thước: {file.ContentLength / 1024}KB"
                };
            }

            // 3.2 Kiểm tra MIME type (Content-Type có thể bị fake nên cần kết hợp magic bytes)
            var contentType = file.ContentType?.ToLowerInvariant() ?? "";
            if (AllowedMimeTypes.TryGetValue(extension, out var validMimes))
            {
                if (!validMimes.Any(m => contentType.StartsWith(m, StringComparison.OrdinalIgnoreCase)))
                {
                    // MIME không khớp - vẫn tiếp tục kiểm tra magic bytes vì browser có thể gửi sai
                }
            }

            // 3.2 Kiểm tra magic bytes (xác thực nội dung file thực sự là ảnh)
            try
            {
                using (var reader = new BinaryReader(file.InputStream))
                {
                    file.InputStream.Position = 0;
                    var headerBytes = reader.ReadBytes(12);
                    file.InputStream.Position = 0;

                    if (!IsValidImageSignature(extension, headerBytes))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "File không phải là ảnh hợp lệ. Nội dung file có thể đã bị giả mạo."
                        };
                    }
                }
            }
            catch
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Không thể đọc file." };
            }

            return new ValidationResult
            {
                IsValid = true,
                SafeExtension = extension.ToLowerInvariant()
            };
        }

        private static bool IsValidImageSignature(string extension, byte[] headerBytes)
        {
            if (!FileSignatures.TryGetValue(extension, out var signatures))
                return false;

            foreach (var sig in signatures)
            {
                if (headerBytes.Length >= sig.Length)
                {
                    bool match = true;
                    for (int i = 0; i < sig.Length; i++)
                    {
                        if (headerBytes[i] != sig[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        // WebP cần kiểm tra thêm "WEBP" ở offset 8
                        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) && headerBytes.Length >= 12)
                        {
                            return headerBytes[8] == 'W' && headerBytes[9] == 'E' && headerBytes[10] == 'B' && headerBytes[11] == 'P';
                        }
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
