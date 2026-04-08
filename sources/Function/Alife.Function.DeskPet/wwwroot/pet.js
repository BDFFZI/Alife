class PetApp {
    constructor() {
        this.app = null;
        this.model = null;
        this.modelName = "";
        this.isSpeaking = false;
        this.lastMouseMoveTime = 0;

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
        this.postMessage({ type: 'ready' });
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

        const ctrl = this.model.internalModel.focusController;
        if (ctrl) {
            ctrl.acceleration = 0.04;
            ctrl.deceleration = 0.08;
        }

        const scale = (window.innerHeight * 0.9) / this.model.height;
        this.model.scale.set(scale);
        this.model.anchor.set(0.5, 0.5);
        this.model.position.set(window.innerWidth / 2, window.innerHeight / 2);
        this.model.interactive = true;
    }

    setupEvents() {
        if (window.chrome?.webview) {
            window.chrome.webview.addEventListener("message", (e) => this.handleHostMessage(e.data));
        }

        window.addEventListener("dblclick", (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                this.performHitTest(e.clientX, e.clientY);
            }
        });

        window.addEventListener("mousedown", async (e) => {
            if (e.button === 0 && (e.target.id === "canvas" || e.target.tagName === "CANVAS")) {
                const hitAreas = await this.model?.hitTest(e.clientX, e.clientY);
                if (!hitAreas || hitAreas.length === 0) this.postMessage({ type: 'drag-request' });
            }
        });

        window.handleMouseMove = (data) => {
            this.lastMouseMoveTime = Date.now();
            this.updateFocus(data.x, data.y);
            this.postMessage({ type: 'mousemove-raw', x: data.x, y: data.y });
        };

        setInterval(() => {
            if (Date.now() - this.lastMouseMoveTime > 3000 && !this.isSpeaking) {
                this.updateFocus(window.innerWidth / 2, window.innerHeight / 2);
            }
        }, 500);

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
            case "load": this.loadModel(msg.url); break;
            case "bubble": this.showBubble(msg.text, msg.duration); break;
            case "expression": this.model?.expression(msg.id); break;
            case "motion": this.model?.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE); break;
            case "look": this.updateFocus(window.innerWidth / 2, window.innerHeight / 2, true); break;
        }
    }

    async performHitTest(x, y) {
        if (!this.model) return;
        const hitAreas = await this.model.hitTest(x, y);
        if (hitAreas.length > 0) {
            this.postMessage({ type: 'hit', areas: hitAreas });
        }
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
                }, 300);
            }, duration);
        }
    }

    updateFocus(x, y, instant = false) {
        if (!this.model) return;
        if (this.isSpeaking && !instant) return;
        this.model.focus(x, y, instant);
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
