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
import json
import os
from PIL import ImageGrab  # Cần cài: pip install Pillow

import sys # Import sys de kiem tra moi truong chay

# --- CẤU HÌNH ---
CHROME_DEBUG_PORT = 9222
GEMINI_URL = "https://gemini.google.com/app"
CONFIG_FILENAME = "config.json"

# Xac dinh duong dan file config
# Neu chay bang file Exe (frozen), ta lay duong dan cua file Exe
# Neu chay bang file Python, ta lay duong dan cua file .py
if getattr(sys, 'frozen', False):
    application_path = os.path.dirname(sys.executable)
else:
    application_path = os.path.dirname(os.path.abspath(__file__))

CONFIG_FILE_PATH = os.path.join(application_path, CONFIG_FILENAME)

# Cấu hình mặc định
DEFAULT_CONFIG = {
    "text_color": "black",
    "background_color": "#abcdef",
    "opacity": 0.5,
    "font_size": 11,
    "position_bottom_offset": 50,
    "position_left_offset": 20,
    "prompt_text": "chỉ trả lời đáp án không giải thích: ",
    "prompt_image": "hãy giải bài trong ảnh, chỉ trả lời đáp án ngắn gọn: "
}

# Load config
config = DEFAULT_CONFIG.copy() # Copy de tranh loi tham chieu
if os.path.exists(CONFIG_FILE_PATH):
    try:
        with open(CONFIG_FILE_PATH, "r", encoding="utf-8") as f:
            valid_config = json.load(f)
            # Update key nào có mới lấy, còn lại giữ default
            for k, v in valid_config.items():
                config[k] = v
        print(f"Da load config tu: {CONFIG_FILE_PATH}")
    except Exception as e:
        print(f"Lỗi đọc config: {e}")
else:
    print(f"Khong tim thay file config tai: {CONFIG_FILE_PATH}. Su dung mac dinh.")

# Hàng đợi để giao tiếp giữa luồng xử lý và giao diện
msg_queue = queue.Queue()

def activate_chrome_window_briefly():
    """
    Kích hoạt Chrome window CỰC NHANH (10ms) để trigger render,
    sau đó TỰ ĐỘNG quay lại cửa sổ đang làm việc.
    """
    try:
        current_window = win32gui.GetForegroundWindow()
        
        def enum_handler(hwnd, results):
            if win32gui.IsWindowVisible(hwnd):
                title = win32gui.GetWindowText(hwnd)
                if "Chrome" in title or "Gemini" in title or "Google Chrome" in title:
                    results.append((hwnd, title))
        
        windows = []
        win32gui.EnumWindows(enum_handler, windows)
        
        if windows:
            chrome_hwnd = windows[0][0]
            if chrome_hwnd != current_window:
                if win32gui.IsIconic(chrome_hwnd):
                    win32gui.ShowWindow(chrome_hwnd, win32con.SW_RESTORE)
                
                win32gui.SetForegroundWindow(chrome_hwnd)
                time.sleep(0.01)  # Flash 10ms
                
                if current_window and win32gui.IsWindow(current_window):
                    win32gui.SetForegroundWindow(current_window)
    except Exception as e:
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
            service = Service(ChromeDriverManager().install())
            self.driver = webdriver.Chrome(service=service, options=chrome_options)
            self.wait = WebDriverWait(self.driver, 30)
            print("Đã kết nối với Chrome!")
        except Exception as e:
            print(f"Không thể kết nối với Chrome: {e}")

    def send_and_get_response(self, text, is_image=False):
        if not self.driver:
            self.connect_browser()
            if not self.driver:
                return "Lỗi: Chưa kết nối được Chrome."

        try:
            # Đảm bảo đang ở tab Gemini
            if "gemini.google.com" not in self.driver.current_url:
                found = False
                for handle in self.driver.window_handles:
                    self.driver.switch_to.window(handle)
                    if "gemini.google.com" in self.driver.current_url:
                        found = True
                        break
                if not found:
                    self.driver.get(GEMINI_URL)

            # --- BƯỚC 1: Đếm phản hồi cũ ---
            initial_responses = self.driver.find_elements(By.CLASS_NAME, "model-response-text")
            initial_count = len(initial_responses)

            # --- BƯỚC 2: GỬI ---
            # Tìm input box
            input_box = self.wait.until(EC.presence_of_element_located((By.XPATH, "//div[@contenteditable='true']")))
            
            # Xóa cũ
            input_box.click()
            input_box.send_keys(Keys.CONTROL, "a")
            input_box.send_keys(Keys.DELETE)
            time.sleep(0.1)
            
            # Logic gửi
            if is_image:
                # Nếu là ảnh: Gõ prompt trước -> Paste ảnh
                prompt_to_use = config["prompt_image"]
                input_box.send_keys(prompt_to_use)
                time.sleep(0.2)
                
                # Paste ảnh từ clipboard
                input_box.send_keys(Keys.CONTROL, 'v')
                
                # CHỜ ẢNH LOAD (QUAN TRỌNG)
                # Gemini sẽ hiện thumbnail ảnh sau khi paste. Ta cần chờ thumbnail này xuất hiện.
                print("Đang chờ ảnh upload...")
                try:
                    # Chờ tối đa 10 giây để thấy thẻ img hoặc dấu hiệu ảnh trong input
                    # Gemini thường hiện ảnh trong 1 div preview
                    # Cách đơn giản nhất: Chờ số lượng thẻ `img` trong input tăng lên
                    WebDriverWait(self.driver, 10).until(
                        lambda d: len(input_box.find_elements(By.TAG_NAME, "img")) > 0 
                        or len(input_box.find_elements(By.CSS_SELECTOR, ".image-preview")) > 0
                    )
                    time.sleep(1) # Chờ thêm 1s cho chắc chắn
                except:
                    print("Không thấy thumbnail ảnh, thử gửi luôn...")
                    time.sleep(3) # Fallback chờ cứng 3s
            else:
                # Nếu là text
                pyperclip.copy(text) 
                input_box.send_keys(Keys.CONTROL, 'v')
                time.sleep(0.5)
            
            # Click gửi
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
                input_box.send_keys(Keys.ENTER)

            print("Đang chờ phản hồi MỚI xuất hiện...")
            
            # --- BƯỚC 3: Chờ phản hồi ---
            end_time = time.time() + 60
            last_text_len = 0
            unchanged_count = 0
            
            old_last_text = ""
            if initial_responses:
                 old_last_text = initial_responses[-1].text.strip()

            while time.time() < end_time:
                try:
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
                            text_resp = new_response.text.strip()
                            
                            if not text_resp:
                                time.sleep(0.2)
                                continue

                            if len(text_resp) > last_text_len:
                                last_text_len = len(text_resp)
                                unchanged_count = 0 
                                time.sleep(0.5)
                                continue
                            elif len(text_resp) == last_text_len and len(text_resp) > 0:
                                unchanged_count += 1
                                if unchanged_count >= 3:
                                    return text_resp
                                time.sleep(0.5)
                                continue
                    else:
                        time.sleep(0.5)
                except Exception as e:
                    pass
            
            return "Không lấy được phản hồi mới (Hết thời gian chờ)."

        except Exception as e:
            return f"Lỗi khi tương tác: {e}"

class OverlayWindow:
    def __init__(self):
        self.root = tk.Tk()
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        
        # Load style từ config
        transparent_color = config["background_color"]
        self.root.configure(bg=transparent_color)
        self.root.wm_attributes("-transparentcolor", transparent_color)
        self.root.attributes("-alpha", config["opacity"]) 

        self.label = tk.Label(
            self.root, 
            text="", 
            fg=config["text_color"],          
            bg=transparent_color, 
            font=("Arial", config["font_size"], "bold"), 
            wraplength=800,      
            justify="left"
        )
        self.label.pack()

        self.screen_h = self.root.winfo_screenheight()
        
        # Mặc định ẩn
        self.root.geometry(f"+50+{self.screen_h - 100}")
        self.root.withdraw()
        
        self.hide_timer = None 

        self.root.after(100, self.check_queue)
        self.root.mainloop()

    def check_queue(self):
        try:
            item = msg_queue.get_nowait()
            if isinstance(item, tuple):
                text, duration = item
                if text == "__HIDE__":
                    self.hide_now()
                else:
                    self.show_message(text, duration)
            else:
                self.show_message(item, 10000)
        except queue.Empty:
            pass
        self.root.after(100, self.check_queue)

    def show_message(self, text, duration_sec):
        if self.hide_timer:
            self.root.after_cancel(self.hide_timer)
            self.hide_timer = None

        self.label.config(text=text)
        self.root.deiconify()
        
        self.root.update_idletasks()
        w = self.label.winfo_reqwidth()
        h = self.label.winfo_reqheight()
        
        # Vị trí từ config
        x_pos = config["position_left_offset"]
        y_pos = self.screen_h - h - config["position_bottom_offset"]
        
        self.root.geometry(f"{w}x{h}+{x_pos}+{y_pos}")
        
        if duration_sec > 0:
            self.hide_timer = self.root.after(int(duration_sec * 1000), self.root.withdraw)
            
    def hide_now(self):
        self.root.withdraw()

def run_overlay():
    OverlayWindow()

last_response_text = ""

def automation_task():
    gemini = GeminiAutomation()
    global last_response_text
    
    print("Script đang chạy...")
    print(f"- Config: text={config['text_color']}, opacity={config['opacity']}")
    
    def on_send_pressed(e):
        global last_response_text
        
        # --- KIỂM TRA CLIPBOARD ---
        # 1. Kiểm tra ảnh trước
        has_image = False
        try:
            img = ImageGrab.grabclipboard()
            if img:
                has_image = True
                print("Phát hiện ẢNH trong clipboard!")
        except:
            pass
            
        # 2. Kiểm tra text
        text_content = pyperclip.paste().strip()
        
        # LOGIC GỬI
        final_prompt = ""
        is_sending_image = False
        
        if has_image:
            # Ưu tiên ảnh
            is_sending_image = True
            msg_queue.put(("...", 2))
            final_prompt = "" # Prompt sẽ lấy từ config trong hàm send
        elif text_content:
            # Nếu không có ảnh, dùng text
            msg_queue.put(("...", 2))
            final_prompt = f"{config['prompt_text']}{text_content}"
        else:
            print("Clipboard trống (không ảnh, không text)!")
            msg_queue.put(("Clipboard trống!", 1))
            return

        result = gemini.send_and_get_response(final_prompt, is_image=is_sending_image)
        last_response_text = result
        msg_queue.put((result, 15))

    def on_replay_pressed(e):
        global last_response_text
        if last_response_text:
            msg_queue.put((last_response_text, 2))
        else:
            msg_queue.put(("Chưa có đáp án nào!", 1))

    def on_hide_pressed(e):
        msg_queue.put(("__HIDE__", 0))

    keyboard.on_press_key("F2", on_send_pressed)
    keyboard.on_press_key("F4", on_replay_pressed)
    keyboard.on_press_key("esc", on_hide_pressed)
    
    keyboard.wait()

if __name__ == "__main__":
    t = Thread(target=automation_task, daemon=True)
    t.start()
    run_overlay()
