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

    function postToHost(data) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(data);
        }
    }

    // --- 核心变量 ---
    let model;
    let modelName = "";
    let comboCount = 0;
    let lastInteractionTime = 0;

    // --- 表情配置 ---
    const EXPS = {
        SMILE: "exp_01",
        BLUSH: "exp_06",
        DIZZY: "exp_04",
        ANGRY: "exp_08",
        SAD: "exp_05",
        SURPRISED: "exp_07",
        CLOSED: "exp_03"
    };

    // --- 动作组映射 ---
    const MOTIONS = {
        IDLE: { group: "Idle", index: 0 },
        PROUD: { group: "TapBody", index: 2 },
        SHY: { group: "TapBody", index: 0 },
        ANNOYED: { group: "TapBody", index: 1 },
        SPECIAL_1: { group: "TapBody", index: 3 }, // 入场专用
        SPECIAL_2: { group: "TapBody", index: 4 },
        SPECIAL_3: { group: "TapBody", index: 5 }
    };

    // --- 台词配置 ---
    const DIALOGUES = {
        head: [
            { text: "哎呀！弄乱真央的发型了喵~ (｡•́︿•̀｡)", exp: EXPS.SAD, mtn: MOTIONS.IDLE },
            { text: "摸摸头... 真央最喜欢主人了喵！ฅ(๑*д*๑)ฅ", exp: EXPS.SURPRISED, mtn: MOTIONS.IDLE },
            { text: "喵？主人是在和真央玩吗？(๑•́ ₃ •̀๑)", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "嘿嘿，主人的手暖暖的喵~", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "呜哇！真央的耳朵不可以用力摸喵！", exp: EXPS.BLUSH, mtn: MOTIONS.SHY },
            { text: "主人摸得真舒服喵，都要睡着了喵... (´-ω-`)", exp: EXPS.CLOSED, mtn: MOTIONS.IDLE },
            { text: "摸头杀！真央的能量补充完毕喵~", exp: EXPS.SMILE, mtn: MOTIONS.PROUD }
        ],
        body: [
            { text: "喵呜！主人不可以乱碰这里喵~ (⁄ ⁄•⁄ω⁄•⁄ ⁄)", exp: EXPS.BLUSH, mtn: MOTIONS.SHY },
            { text: "呜... 那里不可以喵，很痒的喵！( >﹏< )", exp: EXPS.BLUSH, mtn: MOTIONS.ANNOYED },
            { text: "真央今天也很努力在工作喵！(*^▽^*)", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "肚子饿了喵... 突然好想吃小鱼干喵！(﹃ )", exp: EXPS.SURPRISED, mtn: MOTIONS.PROUD },
            { text: "主人的手指... 闻起来有小鱼干的味道喵？", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "不准乱碰喵！不然真央要吃掉主人的钱包买罐罐喵！", exp: EXPS.ANGRY, mtn: MOTIONS.SHY },
            { text: "哼，主人是个大色狼喵！( *・ω・)✄╰ひ╯", exp: EXPS.ANGRY, mtn: MOTIONS.ANNOYED },
            { text: "喵喵喵，这里的毛毛很贵重的喵！", exp: EXPS.BLUSH, mtn: MOTIONS.SHY }
        ],
        random: [
            { text: "喵~ 喵~ 喵~~", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "主人在做什么好玩的事情喵？(◕ᴗ◕✿)", exp: EXPS.SURPRISED, mtn: MOTIONS.IDLE },
            { text: "真央会一直陪着你的喵~ (ฅ´ω`ฅ)", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "呼哇... 突然好想晒太阳喵... (´-ω-`)", exp: EXPS.CLOSED, mtn: MOTIONS.IDLE },
            { text: "主人，记得偶尔要休息一下喵，别太累了喵！", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "真央刚才抓到了一只代码虫子喵！厉害吧喵？", exp: EXPS.SURPRISED, mtn: MOTIONS.PROUD },
            { text: "真央在想，什么时候才能去小鱼干星球喵？", exp: EXPS.SMILE, mtn: MOTIONS.IDLE }
        ],
        combo: [
            { text: "哇啊啊！主人太热情了喵，真央受不了了喵~~ (///Σ///)", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "喵——！！主人快住手喵，真的要晕了喵~~", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "坏主人！要把真央戳坏了喵喵喵！！( >﹏< )", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED },
            { text: "救命喵！主人疯掉了喵！！Σ(っ °Д °;)っ", exp: EXPS.DIZZY, mtn: MOTIONS.SHY }
        ]
    };

    // --- 功能函数 ---

    function showBubble(text, duration = 4000) {
        if (!bubble || !bubbleContainer) return;
        bubble.innerText = text;
        bubbleContainer.style.display = "block";
        setTimeout(() => bubbleContainer.style.opacity = "1", 10);
        
        clearTimeout(window.bubbleTimeout);
        if (duration > 0) {
            window.bubbleTimeout = setTimeout(() => {
                bubbleContainer.style.opacity = "0";
                setTimeout(() => {
                    bubbleContainer.style.display = "none";
                    if (model && model.expression) {
                        model.expression(EXPS.SMILE); 
                    }
                }, 300);
            }, duration);
        }
    }

    async function triggerInteraction(x, y) {
        if (!model) return;
        const hitAreas = await model.hitTest(x, y);
        
        const now = Date.now();
        if (now - lastInteractionTime < 2500) {
            comboCount++;
        } else {
            comboCount = 1;
        }
        lastInteractionTime = now;

        // 强连击判定 (5次以上触发 Poke)
        if (comboCount >= 5 && modelName === "Mao") {
            const diag = DIALOGUES.combo[Math.floor(Math.random() * DIALOGUES.combo.length)];
            model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
            model.expression(diag.exp);
            showBubble(diag.text, 6000);
            
            // 发送 Poke 事件给 AI
            postToHost({ type: "poke", text: `主人对真央进行了连续互动（Combo ${comboCount}），真央现在有点晕头转向喵！` });
            
            comboCount = 0;
            return;
        }

        const h = hitAreas.map(i => i.toLowerCase());
        let category = "random";
        if (hitAreas.length === 0) category = "random";
        else if (h.some(i => i.includes("body"))) category = "body";
        else if (h.some(i => i.includes("head"))) category = "head";

        const pool = DIALOGUES[category];
        const diag = pool[Math.floor(Math.random() * pool.length)];
        
        const shouldPlayMotion = Math.random() < 0.4;
        log(`Interact: ${category} | Combo: ${comboCount} | Motion: ${shouldPlayMotion}`);
        
        if (shouldPlayMotion && diag.mtn) {
            model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
        }
        model.expression(diag.exp);
        showBubble(diag.text);
    }

    // --- 初始化 ---
    await new Promise(r => setTimeout(r, 500));
    const PIXI = window.PIXI;
    const live2d = window.PIXI?.live2d || window.live2d;
    if (!PIXI || !live2d) { log("Error: Missing SDK"); return; }
    const { Live2DModel } = live2d;

    const models = ["models/Mao/Mao.model3.json"];
    
    async function init() {
        const app = new PIXI.Application({
            view: document.getElementById("canvas"),
            autoStart: true,
            resizeTo: window,
            transparent: true,
            backgroundAlpha: 0,
        });

        async function loadModel(url) {
            if (model) app.stage.removeChild(model);
            modelName = url.split('/').slice(-2, -1)[0];
            
            try {
                model = await Live2DModel.from(url);
                app.stage.addChild(model);
                
                const scale = (window.innerHeight * 0.8) / model.height;
                model.scale.set(scale);
                model.anchor.set(0.5, 0.5);
                model.position.set(window.innerWidth / 2, window.innerHeight / 2);
                model.interactive = true;

                setTimeout(() => {
                    if (model && modelName === "Mao") {
                        log("Startup Animation Triggered");
                        model.motion(MOTIONS.SPECIAL_1.group, MOTIONS.SPECIAL_1.index, PIXI.live2d.MotionPriority.FORCE);
                        model.expression(EXPS.SMILE);
                        showBubble("喵~ 主人你回来啦！真央一直在这里等你喵~(ฅ´ω`ฅ)");
                    }
                }, 1000);
                
                log("Pet System Ready.");
                setTimeout(() => logContainer.style.display = "none", 2000);
            } catch (e) {
                log("Load Error: " + e.message);
            }
        }

        // --- 事件绑定 ---

        // A. 接收来自 Host (C#) 的指令
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener("message", (event) => {
                const msg = event.data;
                log(`Host Command: ${msg.type}`);

                if (msg.type === "bubble") {
                    showBubble(msg.text, msg.duration || 4000);
                } else if (msg.type === "expression") {
                    if (model) model.expression(msg.id);
                } else if (msg.type === "motion") {
                    if (model) model.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE);
                } else if (msg.type === "look") {
                    if (model && model.internalModel && model.internalModel.coreModel) {
                        const core = model.internalModel.coreModel;
                        // 强制重置视角参数（通常是这几个，不同模型可能略有不同）
                        ["ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamEyeBallX", "ParamEyeBallY", "ParamBodyAngleX"]
                            .forEach(p => {
                                if (core.getParameterIndex(p) !== -1) core.setParameterValueById(p, 0);
                            });
                    }
                }
            });
        }

        // B. 交互反馈
        window.addEventListener("dblclick", (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                triggerInteraction(e.clientX, e.clientY);
            }
        });

        window.addEventListener("mousedown", async (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                if (!model) return;
                const hitAreas = await model.hitTest(e.clientX, e.clientY);
                if (hitAreas.length === 0) {
                    postToHost({ type: 'drag-request' });
                }
            }
        });

        let lastPos = { x: 0, y: 0 };
        let dist = 0;
        let lastMove = Date.now();
        window.addEventListener("mousemove", (e) => {
            const now = Date.now();
            if (now - lastMove > 100) dist = 0;
            dist += Math.sqrt(Math.pow(e.clientX - lastPos.x, 2) + Math.pow(e.clientY - lastPos.y, 2));
            lastPos = { x: e.clientX, y: e.clientY };
            lastMove = now;

            if (dist > 3500) {
                dist = 0;
                showBubble("呜哇... 别转了，真央要晕了喵！(＠_＠;)");
                if (model && modelName === "Mao") {
                    model.motion(MOTIONS.SHY.group, MOTIONS.SHY.index, PIXI.live2d.MotionPriority.FORCE);
                    model.expression(EXPS.DIZZY);
                    // 发送 Poke 事件给 AI
                    postToHost({ type: "poke", text: "主人在疯狂晃动鼠标，真央被转晕了喵！" });
                }
            }
        });

        const handleSend = () => {
            const msg = chatInput.value.trim();
            if (msg) {
                showBubble("收到！正在转达给 AI 喵~", 2000);
                postToHost({ type: 'chat', text: msg });
                chatInput.value = "";
            }
        };
        sendBtn.onclick = handleSend;
        chatInput.onkeydown = (e) => { if (e.key === "Enter") handleSend(); };
        window.addEventListener("contextmenu", (e) => e.preventDefault());

        loadModel(models[0]);
    }

    init();
})();
