"""
Vision Bridge - 支持 InternVL2.5 / Qwen2.5-VL / Moondream2
长驻进程模式：模型只加载一次，通过 stdin/stdout 与 C# 通信。
"""

import sys
import json
import traceback

# ───────────────────────── 模型类型检测 ─────────────────────────

def detect_model_type(path: str) -> str:
    p = path.lower()
    if "moondream" in p:
        return "moondream"
    if "internvl" in p:
        return "internvl"
    return "qwen"   # 默认 Qwen2.5-VL

# ───────────────────────── 模型加载 ─────────────────────────

def load_model(model_path_or_name: str):
    import torch, os
    device = "cuda" if torch.cuda.is_available() else "cpu"
    dtype  = torch.float16 if device == "cuda" else torch.float32

    load_path = model_path_or_name
    if not os.path.exists(model_path_or_name):
        # 本地路径不存在时的默认 HF ID
        mtype = detect_model_type(model_path_or_name)
        if mtype == "moondream":
            load_path = "vikhyatk/moondream2"
        elif mtype == "internvl":
            load_path = "OpenGVLab/InternVL2_5-1B"
        else:
            load_path = "Qwen/Qwen2.5-VL-3B-Instruct"

    mtype = detect_model_type(load_path)
    print(f"[Bridge] Found device: {device}, dtype: {dtype}", file=sys.stderr, flush=True)
    print(f"[Bridge] Loading {mtype} model from: {load_path}", file=sys.stderr, flush=True)

    # ── Moondream ──
    if mtype == "moondream":
        from transformers import AutoModelForCausalLM, AutoTokenizer
        processor = AutoTokenizer.from_pretrained(load_path, trust_remote_code=True)
        model = AutoModelForCausalLM.from_pretrained(
            load_path, trust_remote_code=True, torch_dtype=dtype
        ).to(device)
        model.eval()
        return model, processor, device, mtype

    # ── InternVL2.5 ──
    if mtype == "internvl":
        from transformers import AutoModel, AutoTokenizer
        processor = AutoTokenizer.from_pretrained(
            load_path, trust_remote_code=True
        )
        # 猴子补丁：临时强制 torch.linspace 在 CPU 上运行，避开 meta tensor 报错
        import torch
        orig_linspace = torch.linspace
        def patched_linspace(*args, **kwargs):
            if "device" not in kwargs:
                kwargs["device"] = "cpu"
            return orig_linspace(*args, **kwargs)
        
        torch.linspace = patched_linspace
        try:
            model = AutoModel.from_pretrained(
                load_path,
                torch_dtype=torch.float32,
                trust_remote_code=True,
                low_cpu_mem_usage=False,
                device_map=None
            )
        finally:
            torch.linspace = orig_linspace
            
        model = model.to(device).to(dtype).eval()
        return model, processor, device, mtype

    # ── Qwen2.5-VL ──
    from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor
    processor = AutoProcessor.from_pretrained(load_path, trust_remote_code=True)
    model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
        load_path,
        torch_dtype=dtype,
        device_map="auto",
        trust_remote_code=True,
    ).eval()
    return model, processor, device, mtype

# ───────────────────────── 推理核心 ─────────────────────────

def run_vision_query(model, processor, device: str, mtype: str,
                     image_path: str, question: str, max_new_tokens: int = 512) -> str:
    import torch
    from PIL import Image

    # ── Moondream ──
    if mtype == "moondream":
        image = Image.open(image_path)
        enc = model.encode_image(image)
        return model.answer_question(enc, question, processor)

    # ── InternVL2.5 ──
    if mtype == "internvl":
        import torchvision.transforms as T
        from torchvision.transforms.functional import InterpolationMode

        IMAGENET_MEAN = (0.485, 0.456, 0.406)
        IMAGENET_STD  = (0.229, 0.224, 0.225)
        transform = T.Compose([
            T.Lambda(lambda img: img.convert("RGB")),
            T.Resize((448, 448), interpolation=InterpolationMode.BICUBIC),
            T.ToTensor(),
            T.Normalize(mean=IMAGENET_MEAN, std=IMAGENET_STD),
        ])

        image = Image.open(image_path).convert("RGB")
        pixel_values = transform(image).unsqueeze(0).to(
            dtype=next(model.parameters()).dtype,
            device=next(model.parameters()).device
        )

        generation_config = dict(max_new_tokens=max_new_tokens, do_sample=False)
        prompt = f"<image>\n{question}"
        with torch.no_grad():
            response = model.chat(processor, pixel_values, prompt, generation_config)
        return response.strip()

    # ── Qwen2.5-VL ──
    from qwen_vl_utils import process_vision_info
    messages = [{
        "role": "user",
        "content": [
            {"type": "image", "image": image_path, "max_pixels": 250880},
            {"type": "text",  "text": question},
        ],
    }]
    text = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    image_inputs, video_inputs = process_vision_info(messages)
    inputs = processor(
        text=[text], images=image_inputs, videos=video_inputs,
        padding=True, return_tensors="pt",
    ).to(device)

    with torch.no_grad():
        generated_ids = model.generate(**inputs, max_new_tokens=max_new_tokens)
    generated_ids_trimmed = [
        out[len(inp):] for inp, out in zip(inputs.input_ids, generated_ids)
    ]
    return processor.batch_decode(
        generated_ids_trimmed,
        skip_special_tokens=True,
        clean_up_tokenization_spaces=False,
    )[0].strip()

# ───────────────────────── 请求处理 ─────────────────────────

def handle_request(model, processor, device: str, mtype: str, req: dict) -> dict:
    action     = req.get("action", "")
    image_path = req.get("image_path", "")

    if not image_path:
        return {"status": "error", "message": "image_path is required"}

    if action in ("caption", "describe"):
        default_q = ("Please describe this image in detail."
                     if mtype == "moondream" else "请用中文详细描述这张图片的内容。")
    elif action == "query":
        default_q = ("What is in this image?"
                     if mtype == "moondream" else "这张图片里有什么？")
    else:
        return {"status": "error", "message": f"Unknown action: {action}"}

    question = req.get("question", default_q)
    max_new_tokens = req.get("max_new_tokens", 512)

    print(f"[Bridge] Request: action={action}, max_tokens={max_new_tokens}", file=sys.stderr, flush=True)

    result = run_vision_query(model, processor, device, mtype, image_path, question, max_new_tokens)
    return {"status": "ok", "result": result}

# ───────────────────────── 主循环 ─────────────────────────

def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--model_path", type=str, default="OpenGVLab/InternVL2_5-1B")
    args = parser.parse_args()

    try:
        model, processor, device, mtype = load_model(args.model_path)
        print(f"[Bridge] Model loaded successfully on {device}", file=sys.stderr, flush=True)
    except Exception:
        print(json.dumps({"status": "error", "message": f"Model load failed: {traceback.format_exc()}"}), flush=True)
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
            response = handle_request(model, processor, device, mtype, req)
        except Exception:
            response = {"status": "error", "message": traceback.format_exc()}
        print(json.dumps(response, ensure_ascii=False), flush=True)

if __name__ == "__main__":
    main()
