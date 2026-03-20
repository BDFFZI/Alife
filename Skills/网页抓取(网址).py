import sys
import json
import time
import os
from bs4 import BeautifulSoup

def scrape(url):
    try:
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            page = browser.new_page()
            page.goto(url, wait_until="networkidle", timeout=60000)
            content = page.content()
            title = page.title()
            browser.close()

            soup = BeautifulSoup(content, "html.parser")
            for tag in soup(['script', 'style', 'nav', 'footer', 'header', 'aside']):
                tag.decompose()
            text = soup.get_text(separator='\n', strip=True)

            # 定位系统缓存目录 (AppData/Local/Alife/Cache)
            cache_root = os.path.join(os.environ.get('LOCALAPPDATA', os.environ.get('TEMP')), 'Alife', 'Cache')
            if not os.path.exists(cache_root):
                os.makedirs(cache_root)
            
            safe_title = "".join([c for c in title if c.isalnum() or c in (' ', '_')]).rstrip()
            filename = f"web_{int(time.time())}_{safe_title[:15]}.txt"
            full_path = os.path.join(cache_root, filename)
            
            with open(full_path, "w", encoding="utf-8") as f:
                f.write(f"URL: {url}\nTITLE: {title}\nDATE: {time.ctime()}\n\n{text}")
            
            char_count = len(text)
            byte_size = os.path.getsize(full_path) / 1024 # KB

            return {
                "title": title,
                "preview": text[:800] + ("..." if char_count > 800 else ""),
                "full_content_cache": full_path,
                "stats": {
                    "total_chars": char_count,
                    "file_size_kb": round(byte_size, 2)
                },
                "status": "Success"
            }
    except Exception as e:
        return {"error": str(e)}

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "请提供网址参数"}))
    else:
        print(json.dumps(scrape(sys.argv[1]), ensure_ascii=False, indent=2))
