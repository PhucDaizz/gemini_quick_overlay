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

# --- CẤU HÌNH ---
# Để script này hoạt động, bạn cần chạy Chrome với chế độ debugging:
# 1. Tắt hết Chrome đang mở.
# 2. Mở CMD hoặc Run, chạy lệnh sau:
# "C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="C:\selenium\ChromeProfile"
# 3. Đăng nhập vào https://gemini.google.com/app trên cửa sổ Chrome vừa mở.

CHROME_DEBUG_PORT = 9222
GEMINI_URL = "https://gemini.google.com/app"

# Hàng đợi để giao tiếp giữa luồng xử lý và giao di
msg_queue = queue.Queue()

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
            # Lưu handle cửa sổ hiện tại của Python (nếu có thể xác định) để không bị mất focus
            # Tuy nhiên với Selenium, ta cố gắng dùng JS để không active cửa sổ Chrome.

            # Đảm bảo đang ở đúng tab Gemini mà không nhất thiết phải switch_to làm nổi bật cửa sổ
            # Nếu driver chưa trỏ vào tab Gemini, ta mới switch.
            if "gemini.google.com" not in self.driver.current_url:
                found = False
                for handle in self.driver.window_handles:
                    # Switch nhanh để check URL, việc này có thể gây chớp nhẹ nhưng cần thiết 1 lần
                    self.driver.switch_to.window(handle)
                    if "gemini.google.com" in self.driver.current_url:
                        found = True
                        break
                if not found:
                    self.driver.get(GEMINI_URL)

            # --- BƯỚC 1: Đếm số lượng phản hồi hiện có TRƯỚC khi gửi ---
            # Điều này giúp ta biết chính xác khi nào phản hồi MỚI xuất hiện
            initial_responses = self.driver.find_elements(By.CLASS_NAME, "model-response-text")
            initial_count = len(initial_responses)
            print(f"Số lượng phản hồi hiện tại: {initial_count}")

            # --- BƯỚC 2: GỬI TIN NHẮN (Dùng JS để không focus) ---
            
            # --- BƯỚC 2: GỬI TIN NHẮN (Dùng Clipboard Paste - Ổn định nhất cho đa dòng) ---
            
            # Copy nội dung cần gửi (đã bao gồm prompt) vào Clipboard hệ thống
            # Đây là cách giả lập hành vi người dùng tốt nhất, giữ nguyên định dạng dòng
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
            
            # 2. Click nút gửi bằng JS hoặc Enter
            # ... (Giữ nguyên logic click nút gửi)
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
                # Nếu không tìm thấy nút gửi, dùng Enter đã work ở các bước trước
                # Tuy nhiên vì vừa paste multiline, Enter có thể chỉ là xuống dòng.
                # Cần đảm bảo focus và nhấn Enter.
                input_box.send_keys(Keys.ENTER)

            print("Đang chờ phản hồi MỚI xuất hiện...")
            
            # --- BƯỚC 3: Chờ phản hồi MỚI ---
            # Ý tưởng: Input bar nằm ở dưới cùng. Câu trả lời mới nhất sẽ là element "model-response-text" nằm cuối cùng trong DOM.
            # Ta sẽ theo dõi element cuối cùng này.
            
            # VẤN ĐỀ: Gemini dùng SPA (Angular/React), chỉ render đầy đủ DOM khi tab được active.
            # GIẢI PHÁP: Định kỳ switch về tab Gemini để trigger render (không làm Chrome nhảy lên)
            
            end_time = time.time() + 60 # Timeout 60s
            last_text_len = 0
            unchanged_count = 0
            loop_count = 0  # Đếm số lần lặp để biết khi nào cần "ping" tab
            
            # Lấy nội dung của câu trả lời cuối cùng TRƯỚC khi gửi (để so sánh)
            old_last_text = ""
            if initial_responses:
                 old_last_text = initial_responses[-1].text.strip()

            while time.time() < end_time:
                loop_count += 1
                
                # Mỗi 3 lần lặp (khoảng 1.5s), switch về tab Gemini và force render
                # Việc này KHÔNG làm Chrome window nhảy lên, chỉ đảm bảo tab active trong Chrome
                if loop_count % 3 == 0:
                    try:
                        # Tìm và switch về tab Gemini
                        for handle in self.driver.window_handles:
                            self.driver.switch_to.window(handle)
                            if "gemini.google.com" in self.driver.current_url:
                                # Force browser repaint bằng cách:
                                # 1. Scroll xuống 1px rồi scroll lên lại (trigger paint)
                                # 2. Hoặc đọc một property gây reflow
                                self.driver.execute_script("""
                                    window.scrollBy(0, 1);
                                    window.scrollBy(0, -1);
                                    // Force reflow
                                    document.body.offsetHeight;
                                """)
                                break
                    except:
                        pass
                
                try:
                    # Lấy danh sách phản hồi hiện tại
                    responses = self.driver.find_elements(By.CLASS_NAME, "model-response-text")
                    current_count = len(responses)
                    
                    # Có 2 trường hợp coi là có phản hồi mới:
                    # 1. Số lượng element tăng lên (thường gặp nhất)
                    # 2. Số lượng không đổi (hiếm, trừ khi chat stream vào div cũ) NHƯNG nội dung text của cái cuối cùng ĐÃ THAY ĐỔI và khác cái cũ.
                    
                    is_new_response = False
                    new_response = None
                    
                    if current_count > initial_count:
                        new_response = responses[-1]
                        is_new_response = True
                    elif current_count == initial_count and current_count > 0:
                        # Check kỹ xem text có đổi không (phòng trường hợp Gemini tái sử dụng div)
                        current_last_text = responses[-1].text.strip()
                        if current_last_text != old_last_text and len(current_last_text) > 0:
                             # Text khác text cũ -> Có vẻ là đang trả lời
                             new_response = responses[-1]
                             is_new_response = True

                    if is_new_response and new_response:
                        # Kiểm tra xem nó có đang hiển thị
                        if new_response.is_displayed():
                            text = new_response.text.strip()
                            
                            # Nếu chưa có text (đang loading)
                            if not text:
                                time.sleep(0.2)
                                continue

                            # Logic chờ dòng text hoàn thiện (streaming)
                            # Nếu text có độ dài thay đổi -> đang stream -> reset bộ đếm chờ
                            if len(text) > last_text_len:
                                last_text_len = len(text)
                                unchanged_count = 0 
                                # In thử 1 đoạn để debug
                                # print(f"Streaming... {len(text)} chars")
                                time.sleep(0.5)
                                continue
                            elif len(text) == last_text_len and len(text) > 0:
                                # Text không đổi sau 0.5s -> bắt đầu đếm
                                unchanged_count += 1
                                # Nếu text không đổi trong 3 lần lặp (1.5s) -> Coi như xong
                                if unchanged_count >= 3:
                                    return text
                                time.sleep(0.5)
                                continue
                    else:
                        # Vẫn chưa thấy dấu hiệu phản hồi mới
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
        self.root.overrideredirect(True) # Mất khung viền
        self.root.attributes("-topmost", True)
        self.root.attributes("-alpha", 0.9) # Trong suốt nhẹ
        self.root.configure(bg="#202124")
        
        self.label = tk.Label(self.root, text="", fg="white", bg="#202124", font=("Arial", 10), wraplength=400, justify="left", padx=10, pady=10)
        self.label.pack()

        # Đặt vị trí góc trái dưới
        screen_h = self.root.winfo_screenheight()
        self.root.geometry(f"+10+{screen_h - 150}")
        
        self.root.withdraw() # Ẩn lúc đầu

        self.root.after(100, self.check_queue)
        self.root.mainloop()

    def check_queue(self):
        try:
            msg = msg_queue.get_nowait()
            self.show_message(msg)
        except queue.Empty:
            pass
        self.root.after(100, self.check_queue)

    def show_message(self, text):
        self.label.config(text=text)
        self.root.deiconify()
        # Tự ẩn sau 10 giây
        self.root.after(10000, self.root.withdraw)
        # Cập nhật lại vị trí vì kích thước text thay đổi
        self.root.update_idletasks()
        w = self.root.winfo_width()
        h = self.root.winfo_height()
        screen_h = self.root.winfo_screenheight()
        self.root.geometry(f"{w}x{h}+20+{screen_h - h - 50}")

def run_overlay():
    OverlayWindow()

def automation_task():
    gemini = GeminiAutomation()
    
    print("Script đang chạy... Nhấn DELETE để gửi nội dung clipboard tới Gemini.")
    
    def on_delete_pressed(e):
        # Lấy nội dung clipboard
        content = pyperclip.paste()
        if not content.strip():
            print("Clipboard trống!")
            return

        # Bọc prompt để yêu cầu trả lời ngắn gọn
        final_prompt = f"chỉ trả lời đáp án không giải thích: {content}"

        print(f"Đã nhận lệnh! Nội dung: {content[:20]}...")
        # Gửi nội dung đã bọc prompt
        result = gemini.send_and_get_response(final_prompt)
        
        # Gửi kết quả sang luồng GUI
        msg_queue.put(result)

    # Đăng ký hotkey
    keyboard.on_press_key("delete", on_delete_pressed)
    keyboard.wait()

if __name__ == "__main__":
    # Chạy giao diện ở luồng chính (Tkinter yêu cầu)
    # Chạy logic automation ở luồng riêng
    t = Thread(target=automation_task, daemon=True)
    t.start()
    
    run_overlay()
