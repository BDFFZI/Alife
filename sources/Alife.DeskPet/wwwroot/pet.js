const EXPS = {
    SMILE: "exp_01", BLUSH: "exp_06", DIZZY: "exp_04",
    ANGRY: "exp_08", SAD: "exp_05", SURPRISED: "exp_07", CLOSED: "exp_03"
};

const MOTIONS = {
    IDLE: { group: "Idle", index: 0 },
    PROUD: { group: "TapBody", index: 2 },
    SHY: { group: "TapBody", index: 0 },
    ANNOYED: { group: "TapBody", index: 1 },
    STARTUP: { group: "TapBody", index: 3 },
    TRAVERSE: { group: "TapBody", index: 4 }
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

class PetApp {
    constructor() {
        this.app = null;
        this.model = null;
        this.modelName = "";
        this.isSpeaking = false;
        this.comboCount = 0;
        this.lastInteractionTime = 0;
        this.lastMouseMoveTime = 0;
        this.activeTweens = new Map();

        // UI references
        this.ui = {
            log: document.getElementById("log-container"),
            bubble: document.getElementById("bubble"),
            bubbleContainer: document.getElementById("bubble-container"),
            chatInput: document.getElementById("chat-input"),
            sendBtn: document.getElementById("send-btn")
        };

        this.bubbleHideTimeout = null;
        this.bubbleDisplayTimeout = null;
    }

    async start() {
        await new Promise(r => setTimeout(r, 500));
        if (!window.PIXI || !window.PIXI.live2d) return;

        this.app = new PIXI.Application({
            view: document.getElementById("canvas"),
            autoStart: true,
            resizeTo: window,
            transparent: true,
            backgroundAlpha: 0,
        });

        this.setupEvents();
        await this.loadModel("models/Mao/Mao.model3.json");
    }

    async loadModel(url) {
        try {
            if (this.model) this.app.stage.removeChild(this.model);
            this.modelName = url.split('/').slice(-2, -1)[0];

            this.model = await PIXI.live2d.Live2DModel.from(url, { autoInteract: false });
            this.app.stage.addChild(this.model);

            this.setupLive2D();
            this.log("Pet System Ready.");
            setTimeout(() => this.ui.log.style.display = "none", 2000);
        } catch (e) {
            this.log("Load Error: " + e.message);
        }
    }

    setupLive2D() {
        // Ticker for tweens
        this.app.ticker.add(() => this.updateTweens());

        // Focus smoothing
        const ctrl = this.model.internalModel.focusController;
        if (ctrl) {
            ctrl.acceleration = 0.04;
            ctrl.deceleration = 0.08;
        }

        // Layout
        const scale = (window.innerHeight * 0.9) / this.model.height;
        this.model.scale.set(scale);
        this.model.anchor.set(0.5, 0.5);
        this.model.position.set(window.innerWidth / 2, window.innerHeight / 2);
        this.model.interactive = true;

        // Startup animation
        if (this.modelName === "Mao") {
            setTimeout(() => {
                this.model.motion(MOTIONS.STARTUP.group, MOTIONS.STARTUP.index, PIXI.live2d.MotionPriority.FORCE);
                this.model.expression(EXPS.SMILE);
                this.showBubble("喵~ 主人你回来啦！真央一直在这里等你喵~(ฅ´ω`ฅ)");
            }, 1000);
        }
    }

    setupEvents() {
        // Host Messages
        if (window.chrome?.webview) {
            window.chrome.webview.addEventListener("message", (e) => this.handleHostMessage(e.data));
        }

        // Double click interaction
        window.addEventListener("dblclick", (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                this.handleHit(e.clientX, e.clientY);
            }
        });

        // Drag request
        window.addEventListener("mousedown", async (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                const hitAreas = await this.model?.hitTest(e.clientX, e.clientY);
                if (!hitAreas || hitAreas.length === 0) this.postMessage({ type: 'drag-request' });
            }
        });

        // Global Mouse Move
        let dist = 0;
        let lastPos = { x: 0, y: 0 };
        let lastMove = Date.now();

        window.handleMouseMove = (data) => {
            const now = Date.now();
            this.lastMouseMoveTime = now;
            this.updateFocus(data.x, data.y);

            const travel = Math.sqrt(Math.pow(data.x - lastPos.x, 2) + Math.pow(data.y - lastPos.y, 2));
            if (now - lastMove > 100) dist *= 0.5;
            dist += travel;
            lastPos = { x: data.x, y: data.y };
            lastMove = now;

            if (dist > 8000) {
                dist = 0;
                this.executePromptInteraction("rotate", "(物理干扰) 用户在疯狂用鼠标转圈圈！");
            }
        };

        // Auto reset focus
        setInterval(() => {
            if (Date.now() - this.lastMouseMoveTime > 3000 && !this.isSpeaking) {
                this.updateFocus(window.innerWidth / 2, window.innerHeight / 2);
            }
        }, 500);

        // Chat Input
        const onSend = () => {
            const text = this.ui.chatInput.value.trim();
            if (text) {
                this.postMessage({ type: 'chat', text });
                this.ui.chatInput.value = "";
            }
        };
        this.ui.sendBtn.onclick = onSend;
        this.ui.chatInput.onkeydown = (e) => { if (e.key === "Enter") onSend(); };
        window.addEventListener("contextmenu", (e) => e.preventDefault());
    }

    handleHostMessage(msg) {
        switch (msg.type) {
            case "bubble": this.showBubble(msg.text, msg.duration); break;
            case "expression": this.model?.expression(msg.id); break;
            case "motion": this.model?.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE); break;
            case "look": this.updateFocus(window.innerWidth / 2, window.innerHeight / 2, true); break;
            case "shake": this.executePromptInteraction("shake", "(物理干扰) 用户在大幅移动你的位置！"); break;
            case "move": this.executePromptInteraction("move", "(物理干扰) 用户在小幅移动你的位置！"); break;
            case "parameter": this.addTween(msg.name, msg.value, msg.duration); break;
            case "window-move":
                if (this.modelName === "Mao") {
                    this.model.motion(MOTIONS.TRAVERSE.group, MOTIONS.TRAVERSE.index, PIXI.live2d.MotionPriority.FORCE);
                }
                break;
        }
    }

    async handleHit(x, y) {
        if (!this.model) return;
        const hitAreas = await this.model.hitTest(x, y);
        const now = Date.now();

        if (now - this.lastInteractionTime < 2500) this.comboCount++;
        else this.comboCount = 1;
        this.lastInteractionTime = now;

        if (this.comboCount >= 5 && this.comboCount % 5 === 0 && this.modelName === "Mao") {
            return this.executePromptInteraction("combo", `(连击干扰) 用户一直在连戳你（Combo ${this.comboCount}）`);
        }

        const areas = hitAreas.map(i => i.toLowerCase());
        let category = "random";
        if (areas.some(i => i.includes("body"))) category = "body";
        else if (areas.some(i => i.includes("head"))) category = "head";

        const pool = DIALOGUES[category];
        const diag = pool[Math.floor(Math.random() * pool.length)];

        if (Math.random() < 0.4 && diag.mtn) {
            this.model.motion(diag.mtn.group, diag.mtn.index, PIXI.live2d.MotionPriority.FORCE);
        }
        this.model.expression(diag.exp);
        this.showBubble(diag.text);
    }

    executePromptInteraction(type, pokeMsg = null) {
        if (!this.model || this.modelName !== "Mao") return;
        const pool = DIALOGUES[type];
        if (!pool) return;

        const diag = pool[Math.floor(Math.random() * pool.length)];
        if (this.model.internalModel?.motionManager) this.model.internalModel.motionManager.stopAllMotions();
        this.model.motion(diag.mtn?.group || "TapBody", diag.mtn?.index || 0, PIXI.live2d.MotionPriority.FORCE);
        this.model.expression(diag.exp || EXPS.SMILE);

        if (pokeMsg) this.postMessage({ type: "poke", text: pokeMsg });
        else this.showBubble(diag.text);
    }

    showBubble(text, duration = 4000) {
        const { bubble, bubbleContainer } = this.ui;
        if (!bubble || !bubbleContainer) return;

        clearTimeout(this.bubbleHideTimeout);
        clearTimeout(this.bubbleDisplayTimeout);

        bubble.innerText = text;
        bubbleContainer.style.display = "block";
        setTimeout(() => bubbleContainer.style.opacity = "1", 10);

        if (duration > 0) {
            this.isSpeaking = true;
            this.updateFocus(window.innerWidth / 2, window.innerHeight / 2, true);

            this.bubbleHideTimeout = setTimeout(() => {
                bubbleContainer.style.opacity = "0";
                this.bubbleDisplayTimeout = setTimeout(() => {
                    bubbleContainer.style.display = "none";
                    this.isSpeaking = false;
                    if (this.model) this.model.expression(EXPS.SMILE);
                }, 300);
            }, duration);
        }
    }

    updateFocus(x, y, instant = false) {
        if (!this.model) return;
        if (this.isSpeaking && !instant) return;
        this.model.focus(x, y, instant);
    }

    addTween(name, targetValue, duration = 1000) {
        if (!this.model) return;
        const core = this.model.internalModel.coreModel;
        this.activeTweens.set(name, {
            start: core.getParameterValueById(name),
            target: targetValue,
            duration: duration,
            hold: 2000,
            startTime: performance.now()
        });
    }

    updateTweens() {
        if (!this.model) return;
        const now = performance.now();
        const core = this.model.internalModel.coreModel;

        for (const [name, t] of this.activeTweens.entries()) {
            const elapsed = now - t.startTime;
            let val = 0;

            if (elapsed < t.duration) {
                const ratio = elapsed / t.duration;
                val = t.start + (t.target - t.start) * (ratio * (2 - ratio));
            } else if (elapsed < t.duration + t.hold) {
                val = t.target;
            } else if (elapsed < t.duration * 2 + t.hold) {
                const ratio = (elapsed - (t.duration + t.hold)) / t.duration;
                val = t.target + (0 - t.target) * (ratio * (2 - ratio));
            } else {
                val = 0;
                this.activeTweens.delete(name);
            }
            core.setParameterValueById(name, val);
        }
    }

    postMessage(data) {
        if (window.chrome?.webview) window.chrome.webview.postMessage(data);
    }

    log(msg) {
        console.log(msg);
        if (this.ui.log) this.ui.log.innerText = msg;
    }
}

const app = new PetApp();
app.start();
