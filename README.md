# 🤖 Gemini Quick Overlay Assistant

**Gemini Quick Assistant** là công cụ hỗ trợ tra cứu nhanh bằng AI (Google Gemini) ngay trên màn hình làm việc mà **KHÔNG CẦN CHUYỂN TAB**, không cần API Key, hoạt động hoàn toàn miễn phí.

Ứng dụng hiển thị câu trả lời dưới dạng **Overlay trong suốt** ở góc màn hình, giúp bạn nắm bắt thông tin nhanh chóng, cực kỳ hữu ích cho việc ôn thi, tra cứu tài liệu nhanh.

## ✨ Tính năng nổi bật

- 🚀 **Tốc độ cao**: Tự động lấy nội dung từ Clipboard và gửi cho Gemini chỉ với 1 phím bấm.
- 👻 **Chế độ tàng hình**: Chrome chạy ngầm hoàn toàn, không chiếm màn hình làm việc.
- 🎨 **Giao diện trong suốt**: Kết quả hiện trên nền trong suốt, chữ đen rõ nét, không che khuất tầm nhìn.
- ⌨️ **Phím tắt tiện lợi**: Thao tác hoàn toàn bằng bàn phím.
- 🔧 **Tự động tìm Chrome**: Tự động dò tìm đường dẫn cài đặt Chrome trên máy.

## 🛠 Yêu cầu hệ thống

- Hệ điều hành: Windows 10/11.
- Trình duyệt: Google Chrome.
- Python 3.8+ (nếu chạy từ source).

## 📦 Cài đặt

### Cách 1: Dành cho người dùng phổ thông (File EXE)
1. Tải file `.exe` từ mục Releases (nếu có).
2. Chạy file `START_TOOL.bat`.

### Cách 2: Dành cho Developer (Chạy từ Source)
1. Clone repo này về máy:
   ```bash
   git clone https://github.com/username/gemini-quick-overlay.git
   cd gemini-quick-overlay
   ```

2. Cài đặt thư viện cần thiết:
   ```bash
   pip install -r requirements.txt
   ```

3. Chạy ứng dụng:
   Click đúp vào `START_TOOL.bat` (khuyên dùng) hoặc chạy lệnh:
   ```bash
   python gemini_helper_v2_window_activation.py
   ```

## 🎮 Hướng dẫn sử dụng

**Bước 1: Khởi động**
- Chạy `START_TOOL.bat`.
- Một cửa sổ Chrome sẽ hiện ra (lần đầu tiên), hãy **Đăng nhập vào Gemini** (https://gemini.google.com).
- Sau khi đăng nhập, bạn có thể minimize Chrome hoặc để nó tự chạy ngầm.

**Bước 2: Thao tác**
1. Bôi đen nội dung câu hỏi/văn bản.
2. Nhấn `Ctrl + C` để Copy.
3. Nhấn **Phím tắt** để ra lệnh cho AI.

**⌨️ Danh sách Phím tắt:**

| Phím | Chức năng |
|------|-----------|
| **F2** | **GỬI CÂU HỎI** (Lấy từ Clipboard -> Gửi Gemini -> Hiện đáp án) |
| **F4** | **XEM LẠI** (Hiển thị lại đáp án cũ trong 2 giây nếu bạn lỡ nhìn không kịp) |
| **ESC** | **ẨN NGAY** (Tắt ngay lập tức lớp phủ đáp án) |

## ⚙️ Cấu hình nâng cao

Nếu máy bạn cài Chrome ở vị trí khác (không phải ổ C mặc định), hãy tạo file `chrome_path.txt` cùng thư mục và dán đường dẫn file `chrome.exe` vào đó. Tool sẽ tự nhận diện.

## ⚠️ Lưu ý

- Đây là công cụ hỗ trợ học tập và tra cứu. Tác giả không chịu trách nhiệm nếu sử dụng sai mục đích.
- Tool hoạt động dựa trên automation giao diện Web Gemini, do đó nếu Google thay đổi giao diện, tool có thể cần được cập nhật.

---
*Made with ❤️ by PĐ*
