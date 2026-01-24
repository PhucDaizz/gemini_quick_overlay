import time
import keyboard
import pyperclip
from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.common.keys import Keys
from webdriver_manager.chrome import ChromeDriverManager
from selenium.webdriver.chrome.service import Service
import tkinter as tk
from threading import Thread
import queue
import win32gui
import win32con

# --- CẤU HÌNH ---
CHROME_DEBUG_PORT = 9222
GEMINI_URL = "https://gemini.google.com/app"

# Hàng đợi để giao tiếp giữa luồng xử lý và giao diện
msg_queue = queue.Queue()

def activate_chrome_window_briefly():
    """
    Kích hoạt Chrome window CỰC NHANH (10ms) để trigger render,
    sau đó TỰ ĐỘNG quay lại cửa sổ đang làm việc.
    """
    try:
        # LƯU cửa sổ hiện tại đang active
        current_window = win32gui.GetForegroundWindow()
        
        def enum_handler(hwnd, results):
            if win32gui.IsWindowVisible(hwnd):
                title = win32gui.GetWindowText(hwnd)
                # Tìm window có Chrome hoặc Gemini trong title
                if "Chrome" in title or "Gemini" in title or "Google Chrome" in title:
                    results.append((hwnd, title))
        
        windows = []
        win32gui.EnumWindows(enum_handler, windows)
        
        if windows:
            # Lấy window đầu tiên (thường là chrome chính)
            chrome_hwnd = windows[0][0]
            
            # Chỉ activate nếu Chrome KHÔNG phải là cửa sổ hiện tại
            # (Tránh gián đoạn nếu user đang ở Chrome sẵn)
            if chrome_hwnd != current_window:
                # Nếu bị minimize thì restore
                if win32gui.IsIconic(chrome_hwnd):
                    win32gui.ShowWindow(chrome_hwnd, win32con.SW_RESTORE)
                
                # Activate Chrome (flash CỰC NHANH)
                win32gui.SetForegroundWindow(chrome_hwnd)
                time.sleep(0.01)  # Flash chỉ 10ms
                
                # QUAN TRỌNG: Quay lại cửa sổ cũ NGAY LẬP TỨC
                if current_window and win32gui.IsWindow(current_window):
                    win32gui.SetForegroundWindow(current_window)
            
    except Exception as e:
        # Bỏ qua lỗi
        pass

class GeminiAutomation:
    def __init__(self):
        self.driver = None
        self.wait = None
        self.connect_browser()

    def connect_browser(self):
        try:
            chrome_options = Options()
            chrome_options.add_experimental_option("debuggerAddress", f"127.0.0.1:{CHROME_DEBUG_PORT}")
            
            # Sử dụng webdriver_manager để tự động tải/tìm chromedriver phù hợp
            service = Service(ChromeDriverManager().install())
            self.driver = webdriver.Chrome(service=service, options=chrome_options)
            
            self.wait = WebDriverWait(self.driver, 30)
            print("Đã kết nối với Chrome!")
        except Exception as e:
            print(f"Không thể kết nối với Chrome: {e}")
            print("Vui lòng đảm bảo bạn đã chạy Chrome với tham số --remote-debugging-port=9222")

    def send_and_get_response(self, text):
        if not self.driver:
            self.connect_browser()
            if not self.driver:
                return "Lỗi: Chưa kết nối được Chrome."

        try:
            # Đảm bảo đang ở đúng tab Gemini
            if "gemini.google.com" not in self.driver.current_url:
                found = False
                for handle in self.driver.window_handles:
                    self.driver.switch_to.window(handle)
                    if "gemini.google.com" in self.driver.current_url:
                        found = True
                        break
                if not found:
                    self.driver.get(GEMINI_URL)

            # --- BƯỚC 1: Đếm số lượng phản hồi hiện có TRƯỚC khi gửi ---
            initial_responses = self.driver.find_elements(By.CLASS_NAME, "model-response-text")
            initial_count = len(initial_responses)
            print(f"Số lượng phản hồi hiện tại: {initial_count}")

            # --- BƯỚC 2: GỬI TIN NHẮN (Dùng Clipboard Paste) ---
            
            # Copy nội dung cần gửi vào Clipboard
            pyperclip.copy(text)
            
            # Tìm input box
            input_box = self.wait.until(EC.presence_of_element_located((By.XPATH, "//div[@contenteditable='true']")))
            
            # Click và Xóa nội dung cũ
            input_box.click()
            input_box.send_keys(Keys.CONTROL, "a")
            input_box.send_keys(Keys.DELETE)
            time.sleep(0.1)
            
            # Dán nội dung mới (Ctrl + V)
            input_box.send_keys(Keys.CONTROL, 'v')
            
            time.sleep(1) # Chờ UI cập nhật trạng thái nút gửi
            
            # Click nút gửi
            js_script_click = """
            var btn = document.evaluate("//button[.//mat-icon[@data-mat-icon-name='send']]", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
            if (btn && !btn.disabled) {
                btn.click();
                return true;
            }
            return false;
            """
            clicked = self.driver.execute_script(js_script_click)
            
            if not clicked:
                print("JS Click không thành công, thử gửi Enter...")
                input_box.send_keys(Keys.ENTER)

            print("Đang chờ phản hồi MỚI xuất hiện...")
            
            # --- BƯỚC 3: Chờ phản hồi MỚI (với Window Activation) ---
            
            end_time = time.time() + 60 # Timeout 60s
            last_text_len = 0
            unchanged_count = 0
            loop_count = 0
            
            # Lấy nội dung của câu trả lời cuối cùng TRƯỚC khi gửi
            old_last_text = ""
            if initial_responses:
                 old_last_text = initial_responses[-1].text.strip()

            while time.time() < end_time:
                try:
                    # Lấy danh sách phản hồi hiện tại
                    responses = self.driver.find_elements(By.CLASS_NAME, "model-response-text")
                    current_count = len(responses)
                    
                    is_new_response = False
                    new_response = None
                    
                    if current_count > initial_count:
                        new_response = responses[-1]
                        is_new_response = True
                    elif current_count == initial_count and current_count > 0:
                        current_last_text = responses[-1].text.strip()
                        if current_last_text != old_last_text and len(current_last_text) > 0:
                             new_response = responses[-1]
                             is_new_response = True

                    if is_new_response and new_response:
                        if new_response.is_displayed():
                            text = new_response.text.strip()
                            
                            if not text:
                                time.sleep(0.2)
                                continue

                            # Chờ text hoàn thiện (streaming)
                            if len(text) > last_text_len:
                                last_text_len = len(text)
                                unchanged_count = 0 
                                time.sleep(0.5)
                                continue
                            elif len(text) == last_text_len and len(text) > 0:
                                unchanged_count += 1
                                if unchanged_count >= 3:
                                    return text
                                time.sleep(0.5)
                                continue
                    else:
                        time.sleep(0.5)
                except Exception as e:
                    print(f"Lỗi khi chờ: {e}")
                    time.sleep(1)
            
            return "Không lấy được phản hồi mới (Hết thời gian chờ)."

        except Exception as e:
            return f"Lỗi khi tương tác: {e}"

class OverlayWindow:
    def __init__(self):
        self.root = tk.Tk()
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        
        # Cấu hình trong suốt
        transparent_color = "#abcdef" 
        self.root.configure(bg=transparent_color)
        self.root.wm_attributes("-transparentcolor", transparent_color)
        
        # Độ mờ
        self.root.attributes("-alpha", 0.5) 

        # Style mới: Chữ đen
        self.label = tk.Label(
            self.root, 
            text="", 
            fg="black",          
            bg=transparent_color, 
            font=("Arial", 11, "bold"), 
            wraplength=800,      
            justify="left"
        )
        self.label.pack()

        # Đặt vị trí
        self.screen_h = self.root.winfo_screenheight()
        
        # Mặc định ẩn
        self.root.geometry(f"+50+{self.screen_h - 100}")
        self.root.withdraw()
        
        self.hide_timer = None # Timer để tự ẩn

        self.root.after(100, self.check_queue)
        self.root.mainloop()

    def check_queue(self):
        try:
            # Lấy tin nhắn: dạng tuple (text, duration)
            item = msg_queue.get_nowait()
            if isinstance(item, tuple):
                text, duration = item
                if text == "__HIDE__":
                    self.hide_now()
                else:
                    self.show_message(text, duration)
            else:
                # Fallback cho code cũ (nếu có)
                self.show_message(item, 10000)
        except queue.Empty:
            pass
        self.root.after(100, self.check_queue)

    def show_message(self, text, duration_sec):
        # Hủy timer ẩn cũ nếu có
        if self.hide_timer:
            self.root.after_cancel(self.hide_timer)
            self.hide_timer = None

        self.label.config(text=text)
        self.root.deiconify()
        
        self.root.update_idletasks()
        w = self.label.winfo_reqwidth()
        h = self.label.winfo_reqheight()
        
        x_pos = 20
        y_pos = self.screen_h - h - 50 
        
        self.root.geometry(f"{w}x{h}+{x_pos}+{y_pos}")
        
        # Hẹn giờ ẩn mới (duration * 1000)
        if duration_sec > 0:
            self.hide_timer = self.root.after(int(duration_sec * 1000), self.root.withdraw)
            
    def hide_now(self):
        self.root.withdraw()

def run_overlay():
    OverlayWindow()

# Biến toàn cục lưu kết quả cuối để xem lại
last_response_text = ""

def automation_task():
    gemini = GeminiAutomation()
    global last_response_text
    
    print("Script đang chạy...")
    print("- Nhấn F2: Gửi câu hỏi (Thay Delete)")
    print("- Nhấn F4: Xem lại đáp án cũ (2 giây)")
    print("- Nhấn ESC: Ẩn đáp án ngay lập tức")
    
    def on_send_pressed(e):
        global last_response_text
        content = pyperclip.paste()
        if not content.strip():
            print("Clipboard trống!")
            return

        final_prompt = f"chỉ trả lời đáp án không giải thích: {content}"
        print(f"Đã nhận lệnh! Nội dung: {content[:20]}...")
        
        # Gửi thông báo "Đang xử lý..." (hiện 2s)
        msg_queue.put(("...", 2)) 
        
        result = gemini.send_and_get_response(final_prompt)
        last_response_text = result # Lưu lại
        
        # Gửi kết quả (hiện 15s)
        msg_queue.put((result, 15))

    def on_replay_pressed(e):
        global last_response_text
        if last_response_text:
            # Hiện lại trong 2s
            msg_queue.put((last_response_text, 2))
        else:
            msg_queue.put(("Chưa có đáp án nào!", 1))

    def on_hide_pressed(e):
        msg_queue.put(("__HIDE__", 0))

    # Đăng ký hotkey
    keyboard.on_press_key("F2", on_send_pressed)
    keyboard.on_press_key("F4", on_replay_pressed)
    keyboard.on_press_key("esc", on_hide_pressed)
    
    keyboard.wait()

if __name__ == "__main__":
    # Chạy giao diện ở luồng chính
    # Chạy logic automation ở luồng riêng
    t = Thread(target=automation_task, daemon=True)
    t.start()
    
    run_overlay()
