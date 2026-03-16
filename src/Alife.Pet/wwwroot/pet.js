(async function() {
    const logContainer = document.getElementById("log-container") || (function() {
        const el = document.createElement("div");
        el.id = "log-container";
        el.style.cssText = "position:fixed; top:10px; left:10px; color:white; background:rgba(0,0,0,0.5); font-family:sans-serif; font-size:12px; pointer-events:none; z-index:9999; padding:5px; border-radius:4px;";
        document.body.appendChild(el);
        return el;
    })();

    const bubbleContainer = document.getElementById("bubble-container");
    const bubble = document.getElementById("bubble");
    const chatInput = document.getElementById("chat-input");
    const sendBtn = document.getElementById("send-btn");

    function log(msg) {
        console.log(msg);
        logContainer.innerText = msg;
    }

    function showBubble(text, duration = 4000) {
        if (!bubble || !bubbleContainer) return;
        bubble.innerText = text;
        bubbleContainer.style.display = "block";
        setTimeout(() => bubbleContainer.style.opacity = "1", 10);
        
        clearTimeout(window.bubbleTimeout);
        if (duration > 0) {
            window.bubbleTimeout = setTimeout(() => {
                bubbleContainer.style.opacity = "0";
                setTimeout(() => bubbleContainer.style.display = "none", 300);
            }, duration);
        }
    }

    await new Promise(r => setTimeout(r, 500));

    const PIXI = window.PIXI;
    const live2d = window.PIXI?.live2d || window.live2d;

    if (!PIXI || !live2d) {
        log(`Error: PIXI or Live2D missing.`);
        return;
    }

    const { Live2DModel } = live2d;
    
    // 模型列表 - Mao 为首选
    const models = [
        "models/Mao/Mao.model3.json",
        "models/Pio/index.json",
        "models/Tia/index.json",
        "models/Wanko/Wanko.model3.json",
        "models/Rice/Rice.model3.json"
    ];
    let currentModelIndex = 0;

    async function init() {
        log("Initializing...");
        const app = new PIXI.Application({
            view: document.getElementById("canvas"),
            autoStart: true,
            resizeTo: window,
            transparent: true,
            backgroundAlpha: 0,
        });

        let model;
        let expressionTimeout;

        async function loadModel(url) {
            if (model) app.stage.removeChild(model);
            const modelName = url.split('/').slice(-2, -1)[0];
            log("Loading: " + modelName);
            
            try {
                model = await Live2DModel.from(url);
                app.stage.addChild(model);
                
                // 适配缩放
                const scale = (window.innerHeight * 0.8) / model.height;
                model.scale.set(scale);
                model.anchor.set(0.5, 0.5);
                model.position.set(window.innerWidth / 2, window.innerHeight / 2);

                model.on("hit", (hitAreas) => {
                    log("Hit: " + hitAreas);
                    const h = hitAreas.map(i => i.toLowerCase());
                    
                    if (modelName === "Mao") {
                        if (h.some(i => i.includes("body"))) {
                            const mtn = `mtn_0${Math.floor(Math.random() * 5) + 1}`;
                            model.motion(mtn);
                        }
                        if (h.some(i => i.includes("head"))) {
                            const exp = `exp_0${Math.floor(Math.random() * 8) + 1}`;
                            model.expression(exp);
                            model.motion("mtn_01");
                            
                            // 5秒后恢复正常表情
                            clearTimeout(expressionTimeout);
                            expressionTimeout = setTimeout(() => {
                                // Live2DModel.expression() 不带参数通常清空当前表情
                                if (model.expression) model.expression(); 
                            }, 5000);
                        }
                    } else {
                        if (h.some(i => i.includes("body"))) {
                            model.motion("TapBody") || model.motion("tap_body") || model.motion("touch_01") || model.motion("Touch1") || model.motion("mtn_01");
                        }
                        if (h.some(i => i.includes("head"))) {
                            model.motion("TapHead") || model.motion("tap_head") || model.motion("shake_01") || model.motion("Touch Dere1") || model.motion("mtn_02");
                        }
                    }
                });

                model.interactive = true;
                log("Ready: " + modelName);
                setTimeout(() => logContainer.style.display = "none", 2000);
                
                // 欢迎语
                if (modelName === "Mao") {
                    showBubble("喵~ 主人你回来啦！真央一直在这里等你喵~(ฅ´ω`ฅ)");
                }
            } catch (e) {
                log("Error: " + e.message);
            }
        }

        // --- 核心交互逻辑 ---

        // 1. 窗口拖动 (通过 postMessage 通知 C#)
        let isMouseDown = false;
        window.addEventListener("mousedown", (e) => {
            // 点击的是画布（不是UI元素）且是左键
            if (e.button === 0 && e.target.id === "canvas") {
                isMouseDown = true;
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({ type: 'drag-request' });
                }
            }
        });
        window.addEventListener("mouseup", () => isMouseDown = false);

        // 2. 眩晕检测 (快速转圈)
        let lastMousePos = { x: 0, y: 0 };
        let totalDistance = 0;
        let lastMoveTime = Date.now();
        window.addEventListener("mousemove", (e) => {
            const now = Date.now();
            const dt = now - lastMoveTime;
            if (dt > 100) { totalDistance = 0; lastMoveTime = now; } // 停顿重置

            const dx = e.clientX - lastMousePos.x;
            const dy = e.clientY - lastMousePos.y;
            const dist = Math.sqrt(dx*dx + dy*dy);
            totalDistance += dist;
            lastMousePos = { x: e.clientX, y: e.clientY };
            lastMoveTime = now;

            if (totalDistance > 3000) { 
                totalDistance = 0;
                showBubble("呜哇... 别转了，真央要晕了喵！(＠_＠;)");
                if (model && modelName === "Mao") {
                    model.motion("mtn_02"); 
                    model.expression("exp_04"); // 假设这个是晕的表情
                }
            }
        });

        // 3. 输入处理
        const handleSend = () => {
            const msg = chatInput.value.trim();
            if (msg) {
                showBubble("正在帮主人处理中... 喵~", 2000);
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({ type: 'chat', text: msg });
                }
                chatInput.value = "";
            }
        };
        sendBtn.onclick = handleSend;
        chatInput.onkeydown = (e) => { if (e.key === "Enter") handleSend(); };

        // 右键切换
        window.addEventListener("contextmenu", (e) => {
            e.preventDefault();
            currentModelIndex = (currentModelIndex + 1) % models.length;
            logContainer.style.display = "block";
            loadModel(models[currentModelIndex]);
        });

        loadModel(models[currentModelIndex]);
    }

    init();
})();
