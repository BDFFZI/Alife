"""
Qwen2.5-VL-3B Vision Bridge
长驻进程模式：模型只加载一次，通过 stdin/stdout 与 C# 通信。

Protocol:
  Input  (stdin, one JSON per line):
    {"action": "caption",  "image_path": "C:/test.jpg"}
    {"action": "query",    "image_path": "C:/test.jpg", "question": "图里有什么？"}
    {"action": "describe", "image_path": "C:/test.jpg"}   # alias for caption

  Output (stdout, one JSON per line):
    {"status": "ok",    "result": "一只猫坐在桌子上。"}
    {"status": "error", "message": "..."}

  Special signals:
    stdout "READY\n" — emitted once when model is loaded and ready
"""

import sys
import json
import traceback


# ───────────────────────── 模型加载 ─────────────────────────

def load_model():
    import torch
    from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor

    device = "cuda" if torch.cuda.is_available() else "cpu"
    dtype = torch.float16 if device == "cuda" else torch.float32

    model_name = "Qwen/Qwen2.5-VL-3B-Instruct"

    processor = AutoProcessor.from_pretrained(model_name)
    model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
        model_name,
        torch_dtype=dtype,
        device_map={"": device},
    )
    model.eval()
    return model, processor, device


# ───────────────────────── 推理核心 ─────────────────────────

def run_vision_query(model, processor, device: str, image_path: str, question: str) -> str:
    import torch
    from PIL import Image
    from qwen_vl_utils import process_vision_info

    messages = [
        {
            "role": "user",
            "content": [
                {"type": "image", "image": image_path},
                {"type": "text",  "text": question},
            ],
        }
    ]

    text = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    image_inputs, video_inputs = process_vision_info(messages)

    inputs = processor(
        text=[text],
        images=image_inputs,
        videos=video_inputs,
        padding=True,
        return_tensors="pt",
    ).to(device)

    with torch.no_grad():
        generated_ids = model.generate(**inputs, max_new_tokens=512)

    generated_ids_trimmed = [
        out_ids[len(in_ids):]
        for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
    ]
    answer = processor.batch_decode(
        generated_ids_trimmed,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )[0]
    return answer.strip()


# ───────────────────────── 请求处理 ─────────────────────────

CAPTION_PROMPT = "请用中文详细描述这张图片的内容。"

def handle_request(model, processor, device: str, req: dict) -> dict:
    action = req.get("action", "")
    image_path = req.get("image_path", "")

    if not image_path:
        return {"status": "error", "message": "image_path is required"}

    if action in ("caption", "describe"):
        question = CAPTION_PROMPT
    elif action == "query":
        question = req.get("question", "这张图片里有什么？")
    else:
        return {"status": "error", "message": f"Unknown action: {action}"}

    result = run_vision_query(model, processor, device, image_path, question)
    return {"status": "ok", "result": result}


# ───────────────────────── 主循环 ─────────────────────────

def main():
    try:
        model, processor, device = load_model()
    except Exception as e:
        print(json.dumps({"status": "error", "message": f"Model load failed: {e}"}), flush=True)
        sys.exit(1)

    # 通知 C# 已就绪
    print("READY", flush=True)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            req = json.loads(line)
        except json.JSONDecodeError as e:
            print(json.dumps({"status": "error", "message": f"JSON parse error: {e}"}), flush=True)
            continue

        try:
            response = handle_request(model, processor, device, req)
        except Exception:
            response = {"status": "error", "message": traceback.format_exc()}

        print(json.dumps(response, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
