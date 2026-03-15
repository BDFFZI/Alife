(async function() {
    const logContainer = document.getElementById("log-container") || (function() {
        const el = document.createElement("div");
        el.id = "log-container";
        el.style.cssText = "position:fixed; top:10px; left:10px; color:white; background:rgba(0,0,0,0.7); font-family:monospace; font-size:12px; pointer-events:none; z-index:9999; padding:10px; max-height:90vh; overflow-y:auto; line-height:1.4;";
        document.body.appendChild(el);
        return el;
    })();

    function log(msg) {
        console.log(msg);
        const line = document.createElement("div");
        line.innerText = `> ${msg}`;
        logContainer.appendChild(line);
    }

    log("Starting deep inspection...");
    await new Promise(r => setTimeout(r, 500));

    const PIXI = window.PIXI;
    if (!PIXI) {
        log("CRITICAL ERROR: PIXI is missing from window!");
        return;
    }

    log("PIXI found.");
    
    // 探测 PIXI.live2d 内部
    if (PIXI.live2d) {
        const keys = Object.keys(PIXI.live2d);
        log("PIXI.live2d sub-keys: " + (keys.length > 0 ? keys.join(", ") : "EMPTY"));
    } else {
        log("PIXI.live2d is MISSING.");
    }

    // 穷举搜索
    let FoundModel = null;
    const searchTargets = [
        { path: "PIXI.live2d.Live2DModel", val: PIXI.live2d?.Live2DModel },
        { path: "PIXI.Live2DModel", val: PIXI.Live2DModel },
        { path: "window.Live2DModel", val: window.Live2DModel },
        { path: "window.live2d.Live2DModel", val: window.live2d?.Live2DModel }
    ];

    for (const target of searchTargets) {
        if (target.val) {
            FoundModel = target.val;
            log(`MATCH FOUND: ${target.path}`);
            break;
        }
    }

    if (!FoundModel) {
        log("STILL NOT FOUND. Attempting fuzzy search...");
        const allPossible = [window, PIXI, PIXI.live2d].filter(Boolean);
        for (const obj of allPossible) {
            for (const key in obj) {
                if (key.toLowerCase() === "live2dmodel") {
                    FoundModel = obj[key];
                    log(`FUZZY MATCH: Found as '${key}' in some object.`);
                    break;
                }
            }
            if (FoundModel) break;
        }
    }

    if (!FoundModel) {
        log("ERROR: Live2DModel is completely elusive.");
        return;
    }

    const modelUrl = "models/Rice/Rice.model3.json";

    async function init() {
        log("Creating PIXI Application...");
        const app = new PIXI.Application({
            view: document.getElementById("canvas"),
            autoStart: true,
            resizeTo: window,
            backgroundAlpha: 0,
        });

        try {
            log("Loading model: " + modelUrl);
            const model = await FoundModel.from(modelUrl);
            log("SUCCESS: Model active.");
            
            app.stage.addChild(model);
            const scale = (window.innerHeight * 0.8) / model.height;
            model.scale.set(scale);
            model.anchor.set(0.5, 0.5);
            model.position.set(window.innerWidth / 2, window.innerHeight / 2);

            log("Renderer ready.");
            setTimeout(() => logContainer.style.opacity = "0.3", 5000);

        } catch (e) {
            log("LOAD ERROR: " + e.message);
            console.error(e);
        }
    }

    init();
})();
