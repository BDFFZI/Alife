(async function () {
    const logContainer = document.getElementById("log-container") || (function () {
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
    let lastMouseMoveTime = 0;
    let isSpeaking = false;

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
        ],
        rotate: [
            { text: "呜哇... 别转了，真央要变旋风猫娘了喵！(＠_＠;)", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED },
            { text: "天旋地转喵！主人是在把真央当陀螺玩吗喵？", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED },
            { text: "喵呜... 感觉整个世界都在转喵，救命喵！", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED },
            { text: "旋转，跳跃，我不停歇... 停！真央真的不行了喵！", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED }
        ],
        shake: [
            { text: "喵喵喵！别抖了，真央的午饭待会就要吐出来了喵！( >﹏< )", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "地震了喵？！不对，是主人在捣乱喵！别摇了喵！", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "呜呜... 真央要被晃散架了喵，主人快停下喵！", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "这种感觉... 像是在坐破损的海盗船喵，唔喵...", exp: EXPS.DIZZY, mtn: MOTIONS.SHY }
        ],
        move: [
            { text: "出发咯！主人这是要带真央去哪里玩呀？(๑•́ ₃ •̀๑)", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "搬新家了喵？这里的风景看起来不错喵！", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "去冒险喵！只要和主人在一起，去哪里都行喵！", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "这就是传说中的‘瞬间移动’吗喵？真神奇喵！", exp: EXPS.SMILE, mtn: MOTIONS.PROUD }
        ]
    };

    // --- 功能函数 ---

    function showBubble(text, duration = 4000) {
        if (!bubble || !bubbleContainer) return;

        // 清除所有正在进行的隐藏计时器
        clearTimeout(window.bubbleHideTimeout);
        clearTimeout(window.bubbleNoneTimeout);

        bubble.innerText = text;
        bubbleContainer.style.display = "block";
        bubbleContainer.style.transition = "opacity 0.3s";
        // 强制重新渲染并显示
        setTimeout(() => bubbleContainer.style.opacity = "1", 10);

        if (duration > 0) {
            isSpeaking = true;
            // 说话时正视前方
            resetGaze();

            window.bubbleHideTimeout = setTimeout(() => {
                bubbleContainer.style.opacity = "0";
                window.bubbleNoneTimeout = setTimeout(() => {
                    bubbleContainer.style.display = "none";
                    isSpeaking = false;
                    // 彻底清除说话状态后再看鼠标
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

        // 强连击判定 (每 5 次触发一次 Poke)
        if (comboCount >= 5 && comboCount % 5 === 0 && modelName === "Mao") {
            const diag = DIALOGUES.combo[Math.floor(Math.random() * DIALOGUES.combo.length)];
            // 简单重置动作
            if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
            model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
            model.expression(diag.exp);

            // 发送 Poke 事件给 AI
            postToHost({ type: "poke", text: `(连击干扰) 主人一直在连戳真央（Combo ${comboCount}），真央感觉要坏掉了喵！` });
            return; // 【关键】如果是特殊交互，不再弹出本地的随机气泡，防止冲突喵
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

    function resetGaze() {
        if (model && model.internalModel?.coreModel) {
            model.focus(window.innerWidth * 0.5, window.innerHeight * 0.5);
        }
    }


    function init() {
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
                model = await Live2DModel.from(url, { autoInteract: false });
                app.stage.addChild(model);

                const scale = (window.innerHeight * 0.9) / model.height;
                model.scale.set(scale);
                model.anchor.set(0.5, 0.5);
                model.position.set(window.innerWidth / 2, window.innerHeight / 2 + window.innerHeight * -0.08);
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
                    resetGaze();
                } else if (msg.type === "shake") {
                    const pool = DIALOGUES.shake;
                    const diag = pool[Math.floor(Math.random() * pool.length)];
                    // AI 会根据 Poke 回应，这里只播动作
                    if (model && modelName === "Mao") {
                        if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
                        model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
                        model.expression(diag.exp);
                        postToHost({ type: "poke", text: `(物理干扰) 主人在疯狂抖动真央，真央觉得超级晕喵！` });
                    }
                } else if (msg.type === "move") {
                    const pool = DIALOGUES.move;
                    const diag = pool[Math.floor(Math.random() * pool.length)];
                    // AI 会根据 Poke 回应，这里只播动作
                    if (model && modelName === "Mao") {
                        if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
                        model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
                        model.expression(diag.exp);
                        postToHost({ type: "poke", text: `(物理干扰) 主人在挪动真央的位置，真央不知道要去哪里喵。` });
                    }
                } else if (msg.type === "window-move") {
                    if (model && modelName === "Mao") {
                        if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
                        const duration = msg.duration || 2000;
                        // 播放“散步/小跑”动作，并尝试循环（如果支持）
                        // 简单处理：如果时间较长，连播两次或增加循环逻辑
                        model.motion(MOTIONS.SPECIAL_2.group, MOTIONS.SPECIAL_2.index, PIXI.live2d.MotionPriority.FORCE);

                        // 如果位移时间较长，在中间点再补一个动画以维持视觉效果
                        if (duration > 2000) {
                            setTimeout(() => {
                                if (model) model.motion(MOTIONS.SPECIAL_2.group, MOTIONS.SPECIAL_2.index, PIXI.live2d.MotionPriority.FORCE);
                            }, duration / 2);
                        }
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



        const handleSend = () => {
            const msg = chatInput.value.trim();
            if (msg) {
                // 去掉中间提示，直接发送
                postToHost({ type: 'chat', text: msg });
                chatInput.value = "";
            }
        };
        sendBtn.onclick = handleSend;
        chatInput.onkeydown = (e) => { if (e.key === "Enter") handleSend(); };
        window.addEventListener("contextmenu", (e) => e.preventDefault());

        loadModel(models[0]);

        // 视线重置计时器 (如果鼠标 3 秒不动，视线回归正前方)
        setInterval(() => {
            // 条件：鼠标超过 3 秒没动，且当前不是说话状态
            const now = Date.now();
            if (now - lastMouseMoveTime > 3000 && !isSpeaking) {
                resetGaze();
            }
        }, 500);

        let lastPos = { x: 0, y: 0 };
        let dist = 0;
        let lastMove = Date.now();

        window.handleMouseMove = function (data) {
            const now = Date.now();
            lastMouseMoveTime = now;

            // [FIX] 视线追踪逻辑保护：说话期间直接拦截，防止每秒几十次的 resetGaze() 调用导致视觉卡顿
            if (isSpeaking) return;

            if (model) {
                // 将屏幕坐标转换为相对于窗口中心的偏移 (-1 ~ 1)
                model.focus(data.x, data.y);
            }

            // 快速移动检测 (原有逻辑)
            const move = Math.sqrt(Math.pow(data.x - lastPos.x, 2) + Math.pow(data.y - lastPos.y, 2));

            if (now - lastMove > 100) dist *= 0.5; // 恢复适中的衰减
            dist += move;

            lastPos = { x: data.x, y: data.y };
            lastMove = now;

            // 阈值提高到 2500，减少误触
            if (dist > 2500) {
                dist = 0;
                const pool = DIALOGUES.rotate;
                const diag = pool[Math.floor(Math.random() * pool.length)];
                if (model && modelName === "Mao") {
                    if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
                    model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
                    model.expression(diag.exp);
                    postToHost({ type: "poke", text: `(物理干扰) 主人在对着真央疯狂转圈圈，真央感觉变成了旋风猫娘喵！` });
                }
            }
        };
    }

    init();
})();
