"""
Qwen2.5-VL-3B & Moondream2 Vision Bridge
长驻进程模式：模型只加载一次，通过 stdin/stdout 与 C# 通信。
"""

import sys
import json
import traceback

# ───────────────────────── 模型加载 ─────────────────────────

def load_model(model_path_or_name: str):
    import torch
    import os
    device = "cuda" if torch.cuda.is_available() else "cpu"
    dtype = torch.float16 if device == "cuda" else torch.float32

    # 如果传进来的本地文件夹不存在，自动向 HuggingFace 官方拉取
    load_path = model_path_or_name
    if not os.path.exists(model_path_or_name):
        if "moondream" in model_path_or_name.lower():
            load_path = "vikhyatk/moondream2"
        else:
            load_path = "Qwen/Qwen2.5-VL-3B-Instruct"

    print(f"Loading model from: {load_path}", file=sys.stderr, flush=True)

    if "moondream" in load_path.lower():
        from transformers import AutoModelForCausalLM, AutoTokenizer
        processor = AutoTokenizer.from_pretrained(load_path, trust_remote_code=True)
        model = AutoModelForCausalLM.from_pretrained(
            load_path,
            trust_remote_code=True,
            torch_dtype=dtype
        ).to(device)
        model.eval()
        return model, processor, device

    from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor
    processor = AutoProcessor.from_pretrained(load_path, trust_remote_code=True)
    
    model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
        load_path,
        torch_dtype=dtype,
        device_map="auto",
        trust_remote_code=True
    )
    model.eval()
    return model, processor, device

# ───────────────────────── 推理核心 ─────────────────────────

def run_vision_query(model, processor, device: str, image_path: str, question: str) -> str:
    import torch
    from PIL import Image

    if model.__class__.__name__ == "Moondream":
        image = Image.open(image_path)
        enc_image = model.encode_image(image)
        return model.answer_question(enc_image, question, processor)

    from qwen_vl_utils import process_vision_info
    messages = [
        {
            "role": "user",
            "content": [
                {
                    "type": "image", 
                    "image": image_path,
                    "max_pixels": 250880
                },
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

def handle_request(model, processor, device: str, req: dict) -> dict:
    action = req.get("action", "")
    image_path = req.get("image_path", "")

    if not image_path:
        return {"status": "error", "message": "image_path is required"}

    if action in ("caption", "describe"):
        if model.__class__.__name__ == "Moondream":
            question = req.get("question", "Please describe this image in detail.")
        else:
            question = req.get("question", "请用中文详细描述这张图片的内容。")
    elif action == "query":
        if model.__class__.__name__ == "Moondream":
            question = req.get("question", "What is in this image?")
        else:
            question = req.get("question", "这张图片里有什么？")
    else:
        return {"status": "error", "message": f"Unknown action: {action}"}

    result = run_vision_query(model, processor, device, image_path, question)
    return {"status": "ok", "result": result}

# ───────────────────────── 主循环 ─────────────────────────

def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--model_path", type=str, default="Qwen/Qwen2.5-VL-3B-Instruct")
    args = parser.parse_args()

    try:
        model, processor, device = load_model(args.model_path)
    except Exception as e:
        print(json.dumps({"status": "error", "message": f"Model load failed: {e}"}), flush=True)
        sys.exit(1)

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
