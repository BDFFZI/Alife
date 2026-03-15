(async function() {
    const logContainer = document.getElementById("log-container") || (function() {
        const el = document.createElement("div");
        el.id = "log-container";
        el.style.cssText = "position:fixed; top:10px; left:10px; color:white; background:rgba(0,0,0,0.5); font-family:sans-serif; font-size:12px; pointer-events:none; z-index:9999; padding:5px; border-radius:4px;";
        document.body.appendChild(el);
        return el;
    })();

    function log(msg) {
        console.log(msg);
        logContainer.innerText = msg;
    }

    await new Promise(r => setTimeout(r, 500));

    const PIXI = window.PIXI;
    const live2d = window.PIXI?.live2d || window.live2d;

    if (!PIXI || !live2d) {
        log(`Error: PIXI or Live2D missing.`);
        return;
    }

    const { Live2DModel } = live2d;
    
    // 模型列表
    const models = [
        "models/Mao/Mao.model3.json", // 选定的主打模型
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

        async function loadModel(url) {
            if (model) app.stage.removeChild(model);
            const modelName = url.split('/').slice(-2, -1)[0];
            log("Loading: " + modelName);
            
            try {
                model = await Live2DModel.from(url);
                app.stage.addChild(model);
                
                // 自动居中并缩放
                const scale = (window.innerHeight * 0.8) / model.height;
                model.scale.set(scale);
                model.anchor.set(0.5, 0.5);
                model.position.set(window.innerWidth / 2, window.innerHeight / 2);

                // 交互
                model.on("hit", (hitAreas) => {
                    log("Hit: " + hitAreas);
                    const h = hitAreas.map(i => i.toLowerCase());
                    
                    // Mao (Cubism 3/4) 有丰富的表情和动作
                    if (modelName === "Mao") {
                        if (h.some(i => i.includes("body"))) {
                            // 随机触发 mtn_01 到 mtn_05
                            const mtn = `mtn_0${Math.floor(Math.random() * 5) + 1}`;
                            model.motion(mtn);
                        }
                        if (h.some(i => i.includes("head"))) {
                            // 随机触发表情 exp_01 到 exp_08
                            const exp = `exp_0${Math.floor(Math.random() * 8) + 1}`;
                            model.expression(exp);
                            model.motion("mtn_01"); // 配合一个点头动作
                        }
                    } else {
                        // 通用兼容逻辑 (Pio, Tia, Wanko)
                        if (h.some(i => i.includes("body"))) {
                            model.motion("TapBody") || model.motion("tap_body") || model.motion("touch_01") || model.motion("Touch1") || model.motion("mtn_01");
                        }
                        if (h.some(i => i.includes("head"))) {
                            model.motion("TapHead") || model.motion("tap_head") || model.motion("shake_01") || model.motion("Touch Dere1") || model.motion("mtn_02");
                        }
                    }
                });

                // 让它看向鼠标
                model.interactive = true;
                
                log("Ready: " + modelName);
                setTimeout(() => logContainer.style.display = "none", 2000);
            } catch (e) {
                log("Error: " + e.message);
            }
        }

        // 右键切换模型
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
