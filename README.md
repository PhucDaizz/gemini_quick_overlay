# 🤖 Gemini Quick Overlay Assistant

**Gemini Quick Assistant** là công cụ hỗ trợ tra cứu nhanh bằng AI (Google Gemini) ngay trên màn hình làm việc mà **KHÔNG CẦN CHUYỂN TAB**, không cần API Key, hoạt động hoàn toàn miễn phí.

Ứng dụng hiển thị câu trả lời dưới dạng **Overlay trong suốt** ở góc màn hình, giúp bạn nắm bắt thông tin nhanh chóng, cực kỳ hữu ích cho việc ôn thi, tra cứu tài liệu nhanh.

## ✨ Tính năng nổi bật

- 🚀 **Tốc độ cao**: Tự động lấy nội dung từ Clipboard và gửi cho Gemini chỉ với 1 phím bấm.
- �️ **Hỗ trợ HÌNH ẢNH**: Copy ảnh (hoặc chụp màn hình) -> Bấm F2 -> Tool tự gửi ảnh và yêu cầu giải.
- �👻 **Chế độ tàng hình**: Chrome chạy ngầm hoàn toàn, không chiếm màn hình làm việc.
- 🎨 **Giao diện trong suốt**: Kết quả hiện trên nền trong suốt, chữ đen rõ nét, không che khuất tầm nhìn.
- ⚙️ **Tùy chỉnh mạnh mẽ**: Cho phép chỉnh màu sắc, độ mờ, font chữ và cả Prompt (câu lệnh) thông qua file `config.json`.
- ⌨️ **Phím tắt tiện lợi**: Thao tác hoàn toàn bằng bàn phím.
- 🔧 **Tự động tìm Chrome**: Tự động dò tìm đường dẫn cài đặt Chrome trên máy.
- ⚡ **Kết nối CDP trực tiếp**: Không cần chromedriver, khởi động nhanh; tự fallback Selenium nếu CDP không khả dụng.
- 🧩 **Chống chạy trùng**: Tự phát hiện instance đang chạy; ghi log xoay vòng ra `gemini_helper.log`.

## 🛠 Yêu cầu hệ thống

- Hệ điều hành: Windows 10/11.
- Trình duyệt: Google Chrome.
- Python 3.8+ (nếu chạy từ source).

## 📦 Cài đặt

### Cách 1: Dành cho người dùng phổ thông
1. Tải bộ cài (Download ZIP) từ nút **Code** màu xanh ở trên và giải nén.
2. **Bước 1:** Chạy file `START_TOOL.vbs` để bật Chrome ngầm.
3. **Bước 2:** Chạy file `gemini_helper_v2_window_activation.exe` (hoặc tên file bạn đã đổi) để bật Tool.
4. **Tắt Tool:** Chạy `TAT_TOOL.bat` hoặc Task Manager → End task `gemini_helper_v2_window_activation`.

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
   - **Bước 1:** Chạy `START_TOOL.vbs` để bật Chrome ngầm.
   - **Bước 2:** Chạy lệnh sau để bật Tool:
     ```bash
     python gemini_helper_v2_window_activation.py
     ```

### 🗂 Cấu trúc dự án

```text
gemini_helper_v2_window_activation.py   # Entry point (giữ tương thích PyInstaller)
gemini_overlay/
  app.py          # Luồng chính, hotkey, clipboard
  overlay.py      # Cửa sổ overlay Tkinter
  automation.py   # Chọn backend CDP / Selenium
  config.py       # Đọc config.json, hot-reload
  constants.py    # Hằng số & giá trị mặc định
  logging_setup.py
  paths.py
  tray.py         # Tray icon
  win_utils.py    # DPI awareness, single-instance mutex
  backends/
    cdp.py        # CDP client
    gemini_cdp.py # Automation qua CDP
    selenium.py   # Fallback Selenium
```

### 📦 Build EXE

```bash
python -m PyInstaller --noconfirm gemini_helper_v2_window_activation.spec
```

Sau khi build, chép `dist\gemini_helper_v2_window_activation.exe` ra thư mục gốc (cạnh `START_TOOL.vbs` và `config.json`).

## 🎮 Hướng dẫn sử dụng

**Bước 1: Khởi động Chrome (Nền tảng)**
- Chạy file `START_TOOL.vbs`.
- Chrome sẽ khởi động. Nếu là chạy lần đầu, hãy đăng nhập Gemini.

**⛔ LƯU Ý SỐNG CÒN (QUAN TRỌNG):**
> **KHÔNG ĐƯỢC nhấn nút (-) Minimize** để ẩn Chrome.
> Nếu Chrome bị Minimize, Windows sẽ đóng băng nó và tool sẽ **NGỪNG HOẠT ĐỘNG**.
>
> ✅ **Cách đúng:** Hãy cứ để cửa sổ Chrome mở đó, và bật trình duyệt khác hoặc ứng dụng khác **ĐÈ LÊN** nó. Chrome phải nằm ở dưới cùng (Background) nhưng vẫn ở trạng thái mở (Restored).

**Bước 2: Chạy Tool**
- Chạy file `.exe` (đã tải về) hoặc file code Python.
- Tool sẽ kết nối với Chrome ngầm và sẵn sàng hoạt động.

**Bước 3: Thao tác (Text & Ảnh)**
1. **Đối với Văn bản:** Bôi đen -> `Ctrl + C`.
2. **Đối với Hình ảnh:** Chụp màn hình (Win + Shift + S) hoặc Copy ảnh bất kỳ.
3. Nhấn phím **F2**.
4. Chờ kết quả hiện ở góc màn hình.

**⌨️ Danh sách Phím tắt:**

| Phím | Chức năng |
|------|-----------|
| **F2** | **GỬI (AUTO)**: Tự phát hiện Text hoặc Ảnh trong Clipboard để gửi đi. |
| **F3** | **COPY ĐÁP ÁN**: Copy đáp án gần nhất vào clipboard (không hiện overlay). |
| **F4** | **XEM LẠI**: Hiển thị lại đáp án gần nhất theo `display_seconds`. |
| **ESC** | **ẨN NGAY**: Tắt ngay lập tức lớp phủ đáp án. |

Các phím trên có thể đổi trong `config.json` qua `hotkey_send`, `hotkey_copy`, `hotkey_replay`, `hotkey_hide`.

**Tray icon**
- Icon tray, icon cửa sổ và icon file exe đều lấy từ `AIHelper.ico`.
- Menu tray có: Hiện đáp án, Copy đáp án, Khởi động lại, Thoát.
- Nhấn đúp vào icon tray để hiện lại đáp án gần nhất.

**Tắt Tool**
- Chạy `TAT_TOOL.bat` để tắt tool đang chạy (Chrome vẫn mở).
- Hoặc mở Task Manager → tìm `gemini_helper_v2_window_activation` → End task.
- Nếu bạn đổi tên file `.exe`, sửa lại tên tương ứng trong `TAT_TOOL.bat`.

## ⚙️ Tùy chỉnh (File `config.json`)

Bạn có thể mở file `config.json` bằng Notepad để chỉnh sửa các thông số:

| Tham số | Ý nghĩa | Mặc định |
|---------|---------|----------|
| `text_color` | Màu chữ (ví dụ: "red", "#ff0000", "black") | "black" |
| `background_color` | Màu nền (dạng hex) | "#abcdef" |
| `opacity` | Độ mờ của giao diện (0.1 - 1.0) | 0.5 |
| `font_size` | Cỡ chữ | 11 |
| `position_bottom_offset` | Khoảng cách so với đáy màn hình | 50 |
| `position_left_offset` | Khoảng cách so với cạnh trái | 20 |
| `prompt_text` | Câu lệnh gửi kèm khi copy Text | "chỉ trả lời..." |
| `prompt_image` | Câu lệnh gửi kèm khi copy Ảnh | "hãy giải bài..." |
| `debug_port` | Cổng debug của Chrome (phải khớp với `START_TOOL.vbs`) | 9222 |
| `page_wait` | Thời gian chờ trang Gemini sẵn sàng (giây) | 5 |
| `response_timeout` | Thời gian chờ phản hồi tối đa (giây) | 60 |
| `display_seconds` | Thời gian hiển thị đáp án (giây) | 15 |
| `settle_ms` | Thời gian chờ văn bản ổn định trước khi lấy kết quả (ms) | 1500 |
| `image_wait` | Thời gian chờ ảnh dán vào ô nhập (giây) | 10 |
| `image_max_size` | Cạnh lớn nhất khi nén ảnh trước khi gửi (px) | 1600 |
| `hotkey_send` | Phím gửi | "f2" |
| `hotkey_copy` | Phím copy đáp án | "f3" |
| `hotkey_replay` | Phím xem lại đáp án | "f4" |
| `hotkey_hide` | Phím ẩn overlay | "esc" |
| `show_pending` | Hiện "..." khoảng 2 giây để xác nhận đã nhận câu hỏi (không hiện trạng thái chờ/streaming); đặt `false` để tắt | true |
| `selectors` | Bộ selector DOM của Gemini (chỉ sửa khi Google đổi giao diện) | (mặc định) |

## ⚙️ Cấu hình nâng cao (Chrome Path)

Nếu máy bạn cài Chrome ở vị trí khác (không phải ổ C mặc định), hãy tạo file `chrome_path.txt` cùng thư mục và dán đường dẫn file `chrome.exe` vào đó. Tool sẽ tự nhận diện.

Nếu cổng `debug_port` đang bị ứng dụng khác (VS Code, Electron...) chiếm, tool sẽ báo lỗi thay vì điều khiển nhầm cửa sổ. Hãy đổi `debug_port` trong `config.json`; `START_TOOL.vbs` sẽ tự đọc cổng này.

Nhật ký hoạt động được ghi xoay vòng tại `gemini_helper.log` (tối đa ~4 MB, 3 bản backup).

## 📤 Chia sẻ cho người dùng phổ thông

Chỉ cần gửi một thư mục/ZIP gồm:
- `gemini_helper_v2_window_activation.exe`
- `START_TOOL.vbs`
- `config.json` (tùy chọn, nên dùng bản mặc định)
- `TAT_TOOL.bat`

Không gửi source `.py`, `build/`, `dist/`, file log hoặc các file `.exe` cũ.

## ⚠️ Lưu ý

- Đây là công cụ hỗ trợ học tập và tra cứu. Tác giả không chịu trách nhiệm nếu sử dụng sai mục đích.
- Tool hoạt động dựa trên automation giao diện Web Gemini, do đó nếu Google thay đổi giao diện, tool có thể cần được cập nhật.

---
*Made with ❤️ by PĐ
