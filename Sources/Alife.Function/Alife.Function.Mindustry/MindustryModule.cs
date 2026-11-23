using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Mindustry;

public class MindustryConfig
{
    [Description("Mindustry.exe 所在的游戏根目录。")]
    public string GameDirectory { get; set; } = "";

    [Description("本机 Mindustry 桥接端口")]
    public int Port { get; set; } = 18768;

    [Description("AI 控制的队伍编号。0 表示与玩家同队，2 在 Passage 等地图中通常是另一方。")]
    public int TeamId { get; set; }

    [Description("Mindustry 数据目录。默认使用 %APPDATA%\\Mindustry，便携版请填写其 data 目录。")]
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mindustry");

    [Description("Alife 与游戏 mod 共用的访问令牌")]
    public string Token { get; set; } = "";
}

[Module("Mindustry 陪玩", "读取关卡、基地库存和陪玩单位状态，执行移动、采运和建造任务。",
    defaultCategory: "Alife 官方/创意娱乐",
    editorUI: typeof(MindustryModuleUI))]
public class MindustryModule(XmlFunctionCaller functionCaller, Interactor<MindustryModule> interactor, PluginSystem pluginSystem)
    : ChatBehaviour, IConfigurable<MindustryConfig>
{
    const string PluginId = "Alife.Function.Mindustry";

    public MindustryConfig Configuration { get; set; } = null!;

    [Description("开启 Mindustry 陪玩，加载查看、采运、建造、生产和进攻工具。")]
    public async Task ActivateMindustryAgent()
    {
        if (operationHandler != null)
            throw new Exception("Mindustry 陪玩已经开启，请先关闭。");
        if (IsConfigured() == false)
            throw new Exception("请先在 Mindustry 陪玩模块配置有效的端口、AI 队伍编号和访问令牌。");

        (HttpStatusCode status, string result, _, _) = await SendMindustryStatusAsync();
        if (status == HttpStatusCode.Unauthorized)
            throw new Exception("Mindustry 服务端拒绝访问，请检查访问令牌。");
        if (status != HttpStatusCode.OK)
            throw new Exception(result);
        if (result.StartsWith("当前关卡：", StringComparison.Ordinal) == false)
            throw new Exception(result);

        operationHandler = new XmlHandler(this) { Description = "Mindustry 陪玩操作" };
        functionCaller.RegisterHandler(operationHandler, DocumentMode.Explicit, DestroyCancellationToken);
        interactor.Poke("已开启 Mindustry 陪玩。先调用 MindustryStatus；每次只提交一项操作并等待回报。");
    }

    [Description("关闭 Mindustry 陪玩，卸载游戏操作工具并停止自主陪玩。")]
    public void DeactivateMindustryAgent()
    {
        if (operationHandler == null)
            throw new Exception("Mindustry 陪玩当前未开启。");

        CancelAutoplay();
        functionCaller.UnregisterHandler(operationHandler);
        operationHandler = null;
        interactor.Poke("已关闭 Mindustry 陪玩。");
    }

    [Description("查询 Mindustry 陪玩模块的安装方式和说明位置。")]
    public void MindustrySetup()
    {
        string resources = Path.Combine(pluginSystem.PluginContext.GetPluginDirectoryPath(PluginId), "Resources");
        string bridge = Path.Combine(resources, "Server", "alife-mindustry.jar");
        string serverStart = Path.Combine(resources, "Server", "Start.ps1");
        string guide = Path.Combine(resources, "安装说明.md");

        interactor.Poke(File.Exists(bridge)
            ? $"普通游玩请在「Mindustry 陪玩」模块界面点击“安装 Mod”，再启动 Mindustry 并开房。模块会自动同步端口和令牌。无人值守服务器运行 {serverStart} -ServerJar server-release.jar。安装说明：{guide}"
            : $"当前安装缺少桥接 JAR，请重新安装包含 Resources 的插件包。安装说明：{guide}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出可用关卡。")]
    public Task MindustryMaps() => CallAsync("maps", allowWhileBusy: true);

    [XmlFunction(FunctionMode.OneShot)]
    [Description("仅在阶段切换或需要完整产业、坐标规划时调用；普通任务完成后先依据自主陪玩的精简状态决策。")]
    public Task MindustryStatus() => CallAsync("status", allowWhileBusy: true);

    [XmlFunction(FunctionMode.Content)]
    [Description("全图搜索矿石、建筑或单位。格式：ore/block/unit ID，支持名称片段；结果按离核心距离排序。")]
    public async Task MindustryFind(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("ore" or "block" or "unit")
            || parts[1].Length is < 1 or > 80
            || parts[1].All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false)
        {
            interactor.Poke("搜索格式为类别和ID或名称片段，例如 ore beryllium、block vent、unit stell。");
            return;
        }

        await CallAsync($"find?kind={parts[0]}&id={parts[1]}", allowWhileBusy: true);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看可研究科技；填写 content 时研究该内容ID。")]
    public Task MindustryTech(string? content = null)
    {
        if (string.IsNullOrEmpty(content))
            return CallAsync("tech", allowWhileBusy: true);

        content = content.Trim();
        if (content.Length is < 1 or > 80 || content.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false)
        {
            interactor.Poke("研究格式为科技列表中的内容ID，例如 silicon-arc-furnace。");
            return Task.CompletedTask;
        }

        return CallAsync($"research?content={Uri.EscapeDataString(content)}", HttpMethod.Post, allowWhileBusy: true);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("建造一个建筑。格式：x y 方向 建筑ID。")]
    public async Task MindustryBuild(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || int.TryParse(parts[0], out int x) == false
            || int.TryParse(parts[1], out int y) == false || int.TryParse(parts[2], out int rotation) == false)
        {
            interactor.Poke("建造格式为 x y 方向 建筑ID，例如 281 107 0 duct。");
            return;
        }

        await CallAsync($"build?x={x}&y={y}&rotation={rotation}&block={Uri.EscapeDataString(parts[3])}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("铺设 L 形单格路线。格式：x1 y1 x2 y2 方向 建筑ID。")]
    public async Task MindustryBuildLine(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || int.TryParse(parts[0], out int x1) == false
            || int.TryParse(parts[1], out int y1) == false
            || int.TryParse(parts[2], out int x2) == false
            || int.TryParse(parts[3], out int y2) == false
            || int.TryParse(parts[4], out int rotation) == false)
        {
            interactor.Poke("连续建造格式为起点x 起点y 终点x 终点y 方向 建筑ID，例如 282 118 270 105 0 duct。");
            return;
        }

        await CallAsync($"build-line?x1={x1}&y1={y1}&x2={x2}&y2={y2}&rotation={rotation}&block={Uri.EscapeDataString(parts[5])}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("拆除己方建筑。格式：x y。")]
    public async Task MindustryDeconstruct(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false)
        {
            interactor.Poke("拆除格式为 x y，例如 283 118。");
            return;
        }

        await CallAsync($"deconstruct?x={x}&y={y}", HttpMethod.Post);
    }


    [XmlFunction(FunctionMode.Content)]
    [Description("移动到目标格。格式：x y。")]
    public async Task MindustryMove(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false)
        {
            interactor.Poke("移动格式为 x y，例如 220 90。");
            return;
        }

        await CallAsync($"move?x={x}&y={y}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("采集并运回核心。格式：物资ID 数量（1～999）。")]
    public async Task MindustryMine(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length is < 1 or > 80
            || parts[0].All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false
            || int.TryParse(parts[1], out int amount) == false
            || amount is < 1 or > 999)
        {
            interactor.Poke("采运格式为物资ID和数量，例如 beryllium 10。");
            return;
        }

        await CallAsync($"mine?item={parts[0]}&amount={amount}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("取消当前陪玩任务，已完成部分会保留。")]
    public Task MindustryCancel() => CallAsync("cancel", HttpMethod.Post, allowWhileBusy: true);
    [XmlFunction(FunctionMode.Content)]
    [Description("仅在需要检查局部地形或建筑候选位时勘查网格。格式：x y 半径（1～6）。")]
    public async Task MindustrySurvey(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int radius) == false || radius is < 1 or > 6)
        {
            interactor.Poke("勘查格式为 x y 半径，例如 230 97 3。");
            return;
        }

        await CallAsync($"survey?x={x}&y={y}&radius={radius}", allowWhileBusy: true);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("持续自主游玩。内容为具体目标；空闲时自动收到精简状态。")]
    public Task MindustryAutoplay(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return Task.CompletedTask;

        string objective = context.FullContent.Trim();
        if (objective.Length is < 1 or > 500 || IsConfigured() == false)
        {
            interactor.Poke("请先配置服务端，并提供不超过 500 字的自主陪玩目标。");
            return Task.CompletedTask;
        }

        CancellationTokenSource session;
        lock (autoplayLock)
        {
            if (autoplay != null)
            {
                interactor.Poke("自主陪玩已经开启。切换目标前请先停止自主陪玩。");
                return Task.CompletedTask;
            }

            session = CancellationTokenSource.CreateLinkedTokenSource(DestroyCancellationToken);
            autoplay = session;
        }

        _ = DriveAutoplayAsync(session, objective);
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("停止自主陪玩。当前动作需另调 MindustryCancel。")]
    public void MindustryAutoplayStop()
    {
        lock (autoplayLock)
        {
            if (autoplay == null || autoplay.IsCancellationRequested)
                return;

            autoplay.Cancel();
        }

        interactor.Poke("自主陪玩已停止。");
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("苏普鲁自动产线。内容：copper、lead、coal 或 sand。埃里克尔改用 MindustryBuild。")]
    public async Task MindustrySupply(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string item = context.FullContent.Trim();
        if (item is not ("copper" or "lead" or "coal" or "sand"))
        {
            interactor.Poke("产线支持 copper、lead、coal、sand。");
            return;
        }

        await CallAsync($"supply?item={item}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("守卫并自动补弹。格式：x y 秒数（5～600）。")]
    public async Task MindustryGuard(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int seconds) == false || seconds is < 5 or > 600)
        {
            interactor.Poke("守卫格式为 x y 秒数（5～600）。");
            return;
        }

        await CallAsync($"guard?x={x}&y={y}&seconds={seconds}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("防御就绪且用户允许时，提前开始下一波。")]
    public Task MindustryNextWave() => CallAsync("wave", HttpMethod.Post, allowWhileBusy: true);

    [XmlFunction(FunctionMode.Content)]
    [Description("全部战斗单位进攻敌方建筑。格式：x y。")]
    public async Task MindustryAttack(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false)
        {
            interactor.Poke("进攻格式为敌方建筑的 x y 坐标。");
            return;
        }

        await CallAsync($"attack?x={x}&y={y}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("配置单位工厂。格式：x y 兵种ID。")]
    public async Task MindustryFactory(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false)
        {
            interactor.Poke("工厂配置格式为 x y 兵种ID，例如 300 120 stell。");
            return;
        }

        await CallAsync($"factory?x={x}&y={y}&unit={Uri.EscapeDataString(parts[2])}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("从核心向己方建筑送料。格式：x y 物资ID 数量（1～99）。")]
    public async Task MindustryFactoryFeed(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || parts[2].Length is < 1 or > 80
            || parts[2].All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false
            || int.TryParse(parts[3], out int amount) == false || amount is < 1 or > 99)
        {
            interactor.Poke("工厂供料格式为 x y 物资ID 数量（1～99），物资ID使用 MindustryStatus 中显示的 ID。");
            return;
        }

        await CallAsync($"factory-feed?x={x}&y={y}&item={parts[2]}&amount={amount}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("在两座己方建筑之间转运物资。格式：源x 源y 目标x 目标y 物资ID 数量（1～99）。")]
    public async Task MindustryTransfer(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || int.TryParse(parts[0], out int sourceX) == false
            || int.TryParse(parts[1], out int sourceY) == false
            || int.TryParse(parts[2], out int targetX) == false
            || int.TryParse(parts[3], out int targetY) == false
            || parts[4].Length is < 1 or > 80
            || parts[4].All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false
            || int.TryParse(parts[5], out int amount) == false || amount is < 1 or > 99)
        {
            interactor.Poke("建筑转运格式为源x 源y 目标x 目标y 物资ID 数量（1～99）。");
            return;
        }

        await CallAsync(
            $"transfer?fromX={sourceX}&fromY={sourceY}&toX={targetX}&toY={targetY}&item={parts[4]}&amount={amount}",
            HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("连接普通电力节点。格式：起点x 起点y 目标x 目标y；beam-node 自动连接。")]
    public async Task MindustryPowerLink(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int toX) == false || int.TryParse(parts[3], out int toY) == false)
        {
            interactor.Poke("电力连接格式为 节点x 节点y 目标x 目标y。");
            return;
        }

        await CallAsync($"power-link?x={x}&y={y}&toX={toX}&toY={toY}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("从己方建筑回收硅到核心。格式：x y 数量。")]
    public async Task MindustryCollectSilicon(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false
            || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int amount) == false || amount is < 1 or > 99)
        {
            interactor.Poke("回收硅的格式为 x y 数量（1～99）。");
            return;
        }

        await CallAsync($"collect-silicon?x={x}&y={y}&amount={amount}", HttpMethod.Post);
    }

    protected override Task OnAwake()
    {
        functionCaller.RegisterHandler(new XmlHandler(new MindustryControl(this)) { Description = "Mindustry 陪玩功能" },
            DocumentMode.Explicit, DestroyCancellationToken);
        return Task.CompletedTask;
    }

    protected override Task OnDestroy()
    {
        if (operationHandler != null)
        {
            functionCaller.UnregisterHandler(operationHandler);
            operationHandler = null;
        }
        CancelAutoplay();
        return Task.CompletedTask;
    }

    sealed class MindustryControl(MindustryModule module)
    {
        [XmlFunction(FunctionMode.OneShot)]
        [Description("开启 Mindustry 陪玩，和用户一起玩 Mindustry。")]
        public Task ActivateMindustryAgent() => module.ActivateMindustryAgent();

        [XmlFunction(FunctionMode.OneShot)]
        [Description("关闭 Mindustry 陪玩，卸载游戏操作工具并停止自主陪玩。")]
        public void DeactivateMindustryAgent() => module.DeactivateMindustryAgent();

        [XmlFunction(FunctionMode.OneShot)]
        [Description("查询 Mindustry 陪玩模块的安装方式和说明位置。")]
        public void MindustrySetup() => module.MindustrySetup();
    }

    readonly object autoplayLock = new();
    readonly object taskLock = new();
    CancellationTokenSource? autoplay;
    XmlHandler? operationHandler;
    long activeTaskId;

    static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    async Task CallAsync(string path, HttpMethod? method = null, bool allowWhileBusy = false)
    {
        if (IsConfigured() == false)
        {
            interactor.Poke("请先在 Mindustry 陪玩模块配置有效的端口、AI 队伍编号和访问令牌。");
            return;
        }

        if (allowWhileBusy == false)
        {
            long taskId;
            lock (taskLock)
                taskId = activeTaskId;
            if (taskId != 0)
            {
                interactor.Poke($"Mindustry 任务 #{taskId} 仍在执行。等待回报、查询状态或取消后再提交下一项。");
                return;
            }
        }

        try
        {
            (HttpStatusCode status, string result, long taskId, long round) = await SendAsync(path, method ?? HttpMethod.Get, DestroyCancellationToken);
            if (taskId != 0)
            {
                lock (taskLock)
                    activeTaskId = taskId;
                _ = ReportCompletionAsync(taskId, round);
                return;
            }

            interactor.Poke(status == HttpStatusCode.Unauthorized
                ? "Mindustry 服务端拒绝访问，请检查令牌。" : result);
        }
        catch (OperationCanceledException) when (DestroyCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (HttpRequestException)
        {
            interactor.Poke("无法连接 Mindustry 陪玩服务，请确认服务器和游戏侧插件已启动。");
        }
        catch (OperationCanceledException)
        {
            interactor.Poke("Mindustry 游戏侧响应超时。查询状态后再决定是否重试。");
        }
    }

    async Task ReportCompletionAsync(long taskId, long round)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), DestroyCancellationToken);
                (HttpStatusCode status, string result, _, _) = await SendAsync($"task?id={taskId}&round={round}", HttpMethod.Get, DestroyCancellationToken);
                if (status == HttpStatusCode.Accepted)
                    continue;

                interactor.Poke($"Mindustry 任务 #{taskId} 回报：{result}\n任务结束。先读取状态，只执行一个操作，等待回报后再继续。");
                return;
            }
        }
        catch (OperationCanceledException) when (DestroyCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            interactor.Poke($"与 Mindustry 的连接中断，任务 #{taskId} 结果未知。重新连接后请先查询状态。");
        }
        finally
        {
            lock (taskLock)
            {
                if (activeTaskId == taskId)
                    activeTaskId = 0;
            }
        }
    }

    async Task DriveAutoplayAsync(CancellationTokenSource session, string objective)
    {
        CancellationToken cancellationToken = session.Token;
        try
        {
            long round = 0;
            string? lastStatus = null;
            while (true)
            {
                string path = round == 0 ? "autonomy" : $"autonomy?round={round}";
                (HttpStatusCode status, string result, _, long currentRound) = await SendAsync(path, HttpMethod.Get, cancellationToken);
                if (status == HttpStatusCode.OK)
                {
                    if (string.Equals(result, lastStatus, StringComparison.Ordinal) == false)
                    {
                        bool isFirstStatus = lastStatus == null;
                        lastStatus = result;
                        interactor.Poke($"{(isFirstStatus ? $"Mindustry 自主陪玩目标：{objective}\n" : "")}{result}\n一次只执行一项任务并等待回报；任务结束后避免例行调用 MindustryStatus；达标或无法继续时停止自主陪玩。");
                    }
                }
                else if (status != HttpStatusCode.Accepted)
                {
                    interactor.Poke($"Mindustry 自主陪玩已结束：{result}");
                    return;
                }

                round = currentRound;
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            interactor.Poke("Mindustry 连接中断，自主陪玩已停止。");
        }
        finally
        {
            lock (autoplayLock)
            {
                autoplay = null;
                session.Dispose();
            }
        }
    }

    bool IsConfigured() => Configuration.Port is >= 1 and <= 65535
        && Configuration.TeamId is >= 0 and <= 255
        && string.IsNullOrWhiteSpace(Configuration.Token) == false;

    async Task<(HttpStatusCode Status, string Result, long TaskId, long Round)> SendMindustryStatusAsync()
    {
        try
        {
            return await SendAsync("status", HttpMethod.Get, DestroyCancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new Exception("无法连接 Mindustry，请确认游戏和桥接 Mod 已启动。", exception);
        }
        catch (OperationCanceledException exception) when (DestroyCancellationToken.IsCancellationRequested == false)
        {
            throw new Exception("Mindustry 游戏侧响应超时，请进入地图后重试。", exception);
        }
    }

    void CancelAutoplay()
    {
        lock (autoplayLock)
            autoplay?.Cancel();
    }

    async Task<(HttpStatusCode Status, string Result, long TaskId, long Round)> SendAsync(
        string path, HttpMethod method, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, $"http://127.0.0.1:{Configuration.Port}/{path}");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Configuration.Token);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string result = await response.Content.ReadAsStringAsync(cancellationToken);
        long taskId = 0;
        if (response.Headers.TryGetValues("X-Mindustry-Task", out IEnumerable<string>? values))
            long.TryParse(values.FirstOrDefault(), out taskId);
        long round = 0;
        if (response.Headers.TryGetValues("X-Mindustry-Round", out IEnumerable<string>? rounds))
            long.TryParse(rounds.FirstOrDefault(), out round);
        return (response.StatusCode, result, taskId, round);
    }
}
