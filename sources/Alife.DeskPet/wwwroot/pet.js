(async function () {
    // --- UI Helpers ---
    const logContainer = document.getElementById("log-container") || createLogContainer();
    const bubbleContainer = document.getElementById("bubble-container");
    const bubble = document.getElementById("bubble");
    const chatInput = document.getElementById("chat-input");
    const sendBtn = document.getElementById("send-btn");

    function createLogContainer() {
        const el = document.createElement("div");
        el.id = "log-container";
        el.style.cssText = "position:fixed; top:10px; left:10px; color:white; background:rgba(0,0,0,0.5); font-family:sans-serif; font-size:12px; pointer-events:none; z-index:9999; padding:5px; border-radius:4px;";
        document.body.appendChild(el);
        return el;
    }

    function log(msg) {
        console.log(msg);
        logContainer.innerText = msg;
    }

    function postToHost(data) {
        if (window.chrome?.webview) {
            window.chrome.webview.postMessage(data);
        }
    }

    // --- Core State ---
    let model;
    let modelName = "";
    let comboCount = 0;
    let lastInteractionTime = 0;
    let lastMouseMoveTime = 0;
    let isSpeaking = false;

    // --- Configuration ---
    const EXPS = {
        SMILE: "exp_01", BLUSH: "exp_06", DIZZY: "exp_04",
        ANGRY: "exp_08", SAD: "exp_05", SURPRISED: "exp_07", CLOSED: "exp_03"
    };

    const MOTIONS = {
        IDLE: { group: "Idle", index: 0 },
        PROUD: { group: "TapBody", index: 2 },
        SHY: { group: "TapBody", index: 0 },
        ANNOYED: { group: "TapBody", index: 1 },
        SPECIAL_1: { group: "TapBody", index: 3 }, // Startup
        SPECIAL_2: { group: "TapBody", index: 4 }, // Walk/Run
        SPECIAL_3: { group: "TapBody", index: 5 }
    };

    const DIALOGUES = {
        head: [
            { text: "哎呀！弄乱真央的发型了喵~ (｡•́︿•̀｡)", exp: EXPS.SAD, mtn: MOTIONS.IDLE },
            { text: "摸摸头... 真央最喜欢主人了喵！ฅ(๑*д*๑)ฅ", exp: EXPS.SURPRISED, mtn: MOTIONS.IDLE },
            { text: "喵？主人是在和真央玩吗？(๑•́ ₃ •̀๑)", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "摸头杀！真央的能量补充完毕喵~", exp: EXPS.SMILE, mtn: MOTIONS.PROUD }
        ],
        body: [
            { text: "喵呜！主人不可以乱碰这里喵~ (⁄ ⁄•⁄ω⁄•⁄ ⁄)", exp: EXPS.BLUSH, mtn: MOTIONS.SHY },
            { text: "真央今天也很努力在工作喵！(*^▽^*)", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "哼，主人是个大色狼喵！( *・ω・)✄╰ひ╯", exp: EXPS.ANGRY, mtn: MOTIONS.ANNOYED }
        ],
        random: [
            { text: "喵~ 喵~ 喵~~", exp: EXPS.SMILE, mtn: MOTIONS.IDLE },
            { text: "真央会一直陪着你的喵~ (ฅ´ω`ฅ)", exp: EXPS.SMILE, mtn: MOTIONS.IDLE }
        ],
        combo: [
            { text: "哇啊啊！主人太热情了喵，真央受不了了喵~~ (///Σ///)", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "喵——！！主人快住手喵，真的要晕了喵~~", exp: EXPS.DIZZY, mtn: MOTIONS.SHY }
        ],
        rotate: [
            { text: "呜哇... 别转了，真央要变旋风猫娘了喵！(＠_＠;)", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED },
            { text: "旋转，跳跃，我不停歇... 停！真央真的不行了喵！", exp: EXPS.DIZZY, mtn: MOTIONS.ANNOYED }
        ],
        shake: [
            { text: "喵喵喵！别抖了，真央的午饭待会就要吐出来了喵！( >﹏< )", exp: EXPS.DIZZY, mtn: MOTIONS.SHY },
            { text: "呜呜... 真央要被晃散架了喵，主人快停下喵！", exp: EXPS.DIZZY, mtn: MOTIONS.SHY }
        ],
        move: [
            { text: "出发咯！主人这是要带真央去哪里玩呀？(๑•́ ₃ •̀๑)", exp: EXPS.SMILE, mtn: MOTIONS.PROUD },
            { text: "这就是传说中的‘瞬间移动’吗喵？真神奇喵！", exp: EXPS.SMILE, mtn: MOTIONS.PROUD }
        ]
    };

    // --- Logic Functions ---

    /**
     * Centralized Bubble & Text Control
     */
    function showBubble(text, duration = 4000) {
        if (!bubble || !bubbleContainer) return;
        clearTimeout(window.bubbleHideTimeout);
        clearTimeout(window.bubbleNoneTimeout);

        bubble.innerText = text;
        bubbleContainer.style.display = "block";
        bubbleContainer.style.transition = "opacity 0.3s";
        setTimeout(() => bubbleContainer.style.opacity = "1", 10);

        if (duration > 0) {
            isSpeaking = true;
            updateModelFocus(window.innerWidth / 2, window.innerHeight / 2, true); // Look center instantly during speech

            window.bubbleHideTimeout = setTimeout(() => {
                bubbleContainer.style.opacity = "0";
                window.bubbleNoneTimeout = setTimeout(() => {
                    bubbleContainer.style.display = "none";
                    isSpeaking = false;
                    if (model) model.expression(EXPS.SMILE);
                }, 300);
            }, duration);
        }
    }

    /**
     * Unified Model Focus Controller
     */
    function updateModelFocus(x, y, instant = false) {
        if (!model) return;
        if (isSpeaking && !instant) return; // Ignore tracking while speaking unless forced (e.g. bubble start)
        model.focus(x, y, instant);
    }

    /**
     * Interaction Logic (Clicks/Hits)
     */
    async function handleHit(x, y) {
        if (!model) return;
        const hitAreas = await model.hitTest(x, y);
        const now = Date.now();

        // Combo detection
        if (now - lastInteractionTime < 2500) comboCount++;
        else comboCount = 1;
        lastInteractionTime = now;

        if (comboCount >= 5 && comboCount % 5 === 0 && modelName === "Mao") {
            return executeSpecialInteraction("combo", `(连击干扰) 用户一直在连戳你（Combo ${comboCount}）`);
        }

        // Area detection
        const h = hitAreas.map(i => i.toLowerCase());
        let category = "random";
        if (h.some(i => i.includes("body"))) category = "body";
        else if (h.some(i => i.includes("head"))) category = "head";

        const pool = DIALOGUES[category];
        const diag = pool[Math.floor(Math.random() * pool.length)];

        if (Math.random() < 0.4 && diag.mtn) {
            model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
        }
        model.expression(diag.exp);
        showBubble(diag.text);
    }

    /**
     * Special State Effects (Shake, Rotate, Move)
     */
    function executeSpecialInteraction(type, pokeMsg = null) {
        if (!model || modelName !== "Mao") return;

        const pool = DIALOGUES[type];
        if (!pool) return;

        const diag = pool[Math.floor(Math.random() * pool.length)];

        if (model.internalModel?.motionManager) model.internalModel.motionManager.stopAllMotions();
        model.motion(diag.mtn?.group || "TapBody", diag.mtn?.index || 0, PIXI.live2d.MotionPriority.FORCE);
        model.expression(diag.exp || EXPS.SMILE);

        if (pokeMsg) postToHost({ type: "poke", text: pokeMsg });
        else showBubble(diag.text);
    }

    // --- Parameter Tweaning ---
    const activeTweens = new Map();

    /**
     * Smoothly transitions a Live2D parameter to a target value, 
     * stays there for a while, and then returns to 0.
     * Logic is moved to the ticker to ensure persistence.
     */
    function updateParameter(name, targetValue, duration = 1000) {
        if (!model) return;
        
        const core = model.internalModel.coreModel;
        activeTweens.set(name, {
            startValue: core.getParameterValueById(name),
            targetValue: targetValue,
            duration: duration,
            holdDuration: 2000,
            startTime: performance.now()
        });
    }

    function processParameterTweens(time) {
        if (!model) return;
        const now = performance.now();
        const core = model.internalModel.coreModel;

        for (const [name, tween] of activeTweens.entries()) {
            const elapsed = now - tween.startTime;
            let current = 0;

            if (elapsed < tween.duration) {
                const t = elapsed / tween.duration;
                const easeT = t * (2 - t);
                current = tween.startValue + (tween.targetValue - tween.startValue) * easeT;
            } else if (elapsed < tween.duration + tween.holdDuration) {
                current = tween.targetValue;
            } else if (elapsed < tween.duration * 2 + tween.holdDuration) {
                const returnElapsed = elapsed - (tween.duration + tween.holdDuration);
                const t = returnElapsed / tween.duration;
                const easeT = t * (2 - t);
                current = tween.targetValue + (0 - tween.targetValue) * easeT;
            } else {
                current = 0;
                activeTweens.delete(name);
            }
            core.setParameterValueById(name, current);
        }
    }

    // --- Initialization & Scene Setup ---

    await new Promise(r => setTimeout(r, 500));
    const PIXI = window.PIXI;
    const live2d = window.PIXI?.live2d || window.live2d;
    if (!PIXI || !live2d) return;

    const app = new PIXI.Application({
        view: document.getElementById("canvas"),
        autoStart: true,
        resizeTo: window,
        transparent: true,
        backgroundAlpha: 0,
    });

    async function loadModel(url) {
        try {
            if (model) app.stage.removeChild(model);
            modelName = url.split('/').slice(-2, -1)[0];

            model = await live2d.Live2DModel.from(url, { autoInteract: false });
            app.stage.addChild(model);

            // Important: Override parameters in the Ticker to ensure they persist
            app.ticker.add(() => processParameterTweens());

            // Refine Gaze Smoothing
            const ctrl = model.internalModel.focusController;
            if (ctrl) {
                // Smoothing parameters: closer to 0 is smoother/slower, closer to 1 is sharper
                ctrl.acceleration = 0.04;
                ctrl.deceleration = 0.08;
                log(`FocusController Tuned: Accel=${ctrl.acceleration}, Decel=${ctrl.deceleration}`);
            }

            // Layout
            const scale = (window.innerHeight * 0.9) / model.height;
            model.scale.set(scale);
            model.anchor.set(0.5, 0.5);
            model.position.set(window.innerWidth / 2, window.innerHeight / 2);
            model.interactive = true;

            // Startup
            setTimeout(() => {
                if (modelName === "Mao") {
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

    // --- Event Listeners ---

    // A. Host Messages
    if (window.chrome?.webview) {
        window.chrome.webview.addEventListener("message", (event) => {
            const msg = event.data;
            switch (msg.type) {
                case "bubble": showBubble(msg.text, msg.duration); break;
                case "expression": model?.expression(msg.id); break;
                case "motion": model?.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE); break;
                case "look": updateModelFocus(window.innerWidth / 2, window.innerHeight / 2, false); break;
                case "shake": executeSpecialInteraction("shake", "(物理干扰) 用户在大幅移动你的位置！"); break;
                case "move": executeSpecialInteraction("move", "(物理干扰) 用户在小幅移动你的位置！"); break;
                case "parameter": updateParameter(msg.name, msg.value, msg.duration); break;
                case "window-move":
                    if (modelName === "Mao") {
                        model.motion(MOTIONS.SPECIAL_2.group, MOTIONS.SPECIAL_2.index, PIXI.live2d.MotionPriority.FORCE);
                    }
                    break;
            }
        });
    }

    // B. Mouse Events
    window.addEventListener("dblclick", (e) => {
        if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
            handleHit(e.clientX, e.clientY);
        }
    });

    window.addEventListener("mousedown", async (e) => {
        if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
            const hitAreas = await model?.hitTest(e.clientX, e.clientY);
            if (!hitAreas || hitAreas.length === 0) postToHost({ type: 'drag-request' });
        }
    });

    // C. Global Mouse Move (Sent from C#)
    let dist = 0;
    let lastPos = { x: 0, y: 0 };
    let lastMove = Date.now();

    window.handleMouseMove = function (data) {
        const now = Date.now();
        lastMouseMoveTime = now;

        updateModelFocus(data.x, data.y);

        // Rotation detection
        const move = Math.sqrt(Math.pow(data.x - lastPos.x, 2) + Math.pow(data.y - lastPos.y, 2));
        if (now - lastMove > 100) dist *= 0.5;
        dist += move;
        lastPos = { x: data.x, y: data.y };
        lastMove = now;

        if (dist > 8000) {
            dist = 0;
            executeSpecialInteraction("rotate", "(物理干扰) 用户在疯狂用鼠标转圈圈！");
        }
    };

    // D. Gaze Auto-Reset
    setInterval(() => {
        if (Date.now() - lastMouseMoveTime > 3000 && !isSpeaking) {
            updateModelFocus(window.innerWidth / 2, window.innerHeight / 2, false);
        }
    }, 500);

    // E. Input Box
    const handleSend = () => {
        const text = chatInput.value.trim();
        if (text) {
            postToHost({ type: 'chat', text });
            chatInput.value = "";
        }
    };
    sendBtn.onclick = handleSend;
    chatInput.onkeydown = (e) => { if (e.key === "Enter") handleSend(); };
    window.addEventListener("contextmenu", (e) => e.preventDefault());

    loadModel("models/Mao/Mao.model3.json");
})();
