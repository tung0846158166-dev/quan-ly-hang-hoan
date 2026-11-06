# 📦 Phần Mềm Quản Lý & Đối Soát Hàng Hoàn Shopee / TikTok Shop (Desktop App)

Hệ thống chuyên dụng dành cho Chủ Shop và Bộ phận Kho vận để quản lý, kiểm đếm và tự động đối soát hàng hoàn từ các sàn TMĐT (Shopee, TikTok Shop, Lazada).

---

## 🚀 1. Cách Chạy Ngay Trên Máy Tính (Không Cần Cài Đặt)

Bạn có thể chạy ngay lập tức mà không cần cài thêm bất kỳ phần mềm hay công cụ lập trình nào:

* **Cách 1 (Nhanh nhất):** Nhấp đúp chuột vào biểu tượng **`Quan Ly Hang Hoan`** ngay trên màn hình Desktop (màn hình chính máy tính).
* **Cách 2:** Vào thư mục dự án và nhấp đúp file **`QuanLyHangHoan.exe`**.
* **Ghim vào Taskbar:** Khi cửa sổ app mở lên, bạn nhấp chuột phải vào biểu tượng ứng dụng ở thanh Taskbar bên dưới màn hình chọn **"Pin to taskbar" (Ghim vào thanh tác vụ)** để những lần sau chỉ cần bấm 1 click là mở.

---

## 🌐 2. Cách Dùng GitHub Để Tự Động Build Ra Bộ Cài Đặt `.exe`

Dự án đã được cấu hình sẵn **GitHub Actions CI/CD** bằng công nghệ **Electron**. Khi bạn đưa code này lên GitHub, máy chủ GitHub sẽ tự động biên dịch và tạo ra file cài đặt `.exe`:

### Các bước thực hiện:
1. Tạo một Repository mới trên tài khoản GitHub của bạn (ví dụ đặt tên: `quan-ly-hang-hoan`).
2. Tải toàn bộ các file trong thư mục này lên GitHub:
   * `Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html`
   * `package.json`
   * `main.js`
   * `app_icon.ico` & `app_icon.png`
   * Thư mục `.github/workflows/build-desktop-app.yml`
3. Sau khi đẩy code lên:
   * Vào tab **Actions** trên GitHub.
   * Bạn sẽ thấy quy trình **Build Windows Desktop App (.exe)** đang tự động chạy.
   * Sau khoảng 2 - 3 phút khi hoàn tất, bạn vào mục **Artifacts** để tải file cài đặt **`QuanLyHangHoan-Setup-1.0.0.exe`** hoặc bản portable về sử dụng.
