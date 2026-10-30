// 聊天类界面只能由浏览器完成的部分：贴底跟随、输入框按键与粘贴、消息区高度拖拽。
// 放在 Alife.Client 的静态资源里：宿主（对话看板）与插件（世界群聊）按固定 URL 共用同一份行为，
// 插件只依赖这个地址，不引用 Alife.Client 工程。
// 用模块而不是内联脚本：组件重建（切换角色、重载插件）时模块状态还在，绑定关系跟着元素本身重建。

const bottomThreshold = 48;

// 按消息区元素 id 登记，宿主面板与插件抽屉可能同时在页面上，各自跟随各自的滚动
const streams = new Map();

function isNearBottom(area) {
    return area.scrollHeight - area.scrollTop - area.clientHeight < bottomThreshold;
}

function setPinned(stream, value) {
    stream.pinned = value;
    stream.body.dataset.atBottom = value ? "true" : "false";
}

// 流式输出每来一个字都会改动 DOM，逐次设 scrollTop 会让滚动条来回跳；
// 一帧内只做一次贴底，且只在用户本来就停在底部时才动。
function stickToBottom(stream) {
    if (stream.pinned == false || stream.frame) return;
    stream.frame = requestAnimationFrame(() => {
        stream.frame = 0;
        stream.area.scrollTop = stream.area.scrollHeight;
    });
}

function release(stream) {
    stream.controller.abort();
    stream.observer.disconnect();
}

export function setupStream(bodyId, streamId) {
    const body = document.getElementById(bodyId);
    const area = document.getElementById(streamId);
    if (!body || !area) return;

    const existing = streams.get(streamId);
    // 同一个元素重复调用（重新渲染、抽屉反复开合）不需要重建
    if (existing && existing.area === area) return;
    if (existing) release(existing);

    // 关掉浏览器自带的滚动锚定，滚动位置只由这套逻辑决定
    area.style.overflowAnchor = "none";

    const controller = new AbortController();
    const stream = {
        body: body,
        area: area,
        controller: controller,
        pinned: true,
        frame: 0,
        observer: new MutationObserver(() => stickToBottom(stream)),
    };
    area.addEventListener("scroll", () => setPinned(stream, isNearBottom(area)), { passive: true, signal: controller.signal });
    stream.observer.observe(area, { childList: true, subtree: true, characterData: true });
    streams.set(streamId, stream);
    setPinned(stream, true);
    area.scrollTop = area.scrollHeight;
}

export function scrollToLatest(streamId) {
    const stream = streams.get(streamId);
    if (!stream) return;

    setPinned(stream, true);
    stream.area.scrollTop = stream.area.scrollHeight;
}

export function setupEnter(elementId, dotNetRef) {
    const area = document.getElementById(elementId);
    if (!area || area.dataset.chatEnterBound === "true") return;

    area.dataset.chatEnterBound = "true";
    // 中文输入法确认候选词的按键同样是 Enter，必须区分“选词”和“发送”，
    // 否则选词时消息会被直接发出去。
    let compositionEndedAt = 0;
    area.addEventListener("compositionstart", () => compositionEndedAt = 0);
    area.addEventListener("compositionend", () => compositionEndedAt = performance.now());
    area.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" || event.shiftKey) return;
        if (event.isComposing || event.keyCode === 229) return;
        if (compositionEndedAt && performance.now() - compositionEndedAt < 50) return;

        event.preventDefault();
        event.stopPropagation();
        dotNetRef.invokeMethodAsync("OnEnterAsync", area.value);
    });
}

export function setupPaste(elementId, dotNetRef) {
    const area = document.getElementById(elementId);
    if (!area || area.dataset.chatPasteBound === "true") return;

    area.dataset.chatPasteBound = "true";
    const readAsBase64 = (file) => new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.split(",")[1]);
        reader.onerror = reject;
        reader.readAsDataURL(file);
    });

    const insertImagePaths = async (items) => {
        const paths = [];
        for (const item of items) {
            const file = item.getAsFile();
            if (!file) continue;

            const base64 = await readAsBase64(file);
            const extension = (item.type.split("/")[1] || "png").replace(/[^\w]/g, "");
            const path = await dotNetRef.invokeMethodAsync("CreateTempImageAsync", base64, extension);
            if (path) paths.push(path);
        }
        if (paths.length > 0) insertAtCursor(area.id, paths.join("\n"));
    };

    area.addEventListener("paste", (event) => {
        event.preventDefault();
        const clipboardData = event.clipboardData;
        if (!clipboardData) return;

        // 从资源管理器复制文件时剪贴板带真实路径，直接写路径
        const files = Array.from(clipboardData.files ?? []).map(f => f.path).filter(p => p);
        if (files.length > 0) {
            insertAtCursor(area.id, files.join("\n"));
            return;
        }

        // 截图之类只有图片数据没有路径的，先落成临时文件再写路径
        const images = Array.from(clipboardData.items ?? []).filter(i => i.kind === "file" && i.type.startsWith("image/"));
        if (images.length > 0) {
            insertImagePaths(images);
            return;
        }

        const text = clipboardData.getData("text") ?? "";
        if (text) insertAtCursor(area.id, text);
    });
}


function editValue(area, text, atEnd) {
    const position = area.value.length;
    const start = atEnd ? position : area.selectionStart;
    const end = atEnd ? position : area.selectionEnd;
    const value = atEnd && position > 0 && !area.value.endsWith("\n") ? "\n" + text : text;
    area.setRangeText(value, start, end, "end");
    area.focus();
    // Blazor 的绑定只听 input 事件，程序化改值必须自己派发一次
    area.dispatchEvent(new Event("input", { bubbles: true }));
}

export function insertAtCursor(elementId, text) {
    const area = document.getElementById(elementId);
    if (area) editValue(area, text, false);
}

export function appendLine(elementId, text) {
    const area = document.getElementById(elementId);
    if (area) editValue(area, text, true);
}

// 发送后清空输入框：清空 DOM 即可，模型那边已经赋过值，不需要重建组件
export function clearInput(elementId) {
    const area = document.getElementById(elementId);
    if (!area) return;

    area.value = "";
    area.focus();
}

export function setupSplitter(splitterId, streamId, minStream, minInput) {
    const splitter = document.getElementById(splitterId);
    if (!splitter || splitter.dataset.chatSplitterBound === "true") return;

    const stream = document.getElementById(streamId);
    const container = splitter.parentElement;
    if (!stream || !container) return;

    splitter.dataset.chatSplitterBound = "true";
    splitter.addEventListener("mousedown", (downEvent) => {
        const startY = downEvent.clientY;
        const startHeight = stream.offsetHeight;
        const maxStreamHeight = container.clientHeight - splitter.offsetHeight - minInput;

        const move = (moveEvent) => {
            stream.style.flex = "none";
            stream.style.height = Math.min(maxStreamHeight, Math.max(minStream, startHeight + moveEvent.clientY - startY)) + "px";
        };
        const up = () => {
            document.body.style.cursor = "";
            document.body.style.userSelect = "";
            document.removeEventListener("mousemove", move);
            document.removeEventListener("mouseup", up);
        };

        document.body.style.cursor = "row-resize";
        document.body.style.userSelect = "none";
        document.addEventListener("mousemove", move);
        document.addEventListener("mouseup", up);
    });
}
