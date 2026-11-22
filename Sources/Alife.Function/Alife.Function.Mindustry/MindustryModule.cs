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
        interactor.Poke("已开启 Mindustry 陪玩。请先调用 MindustryStatus 查看当前局面，再下达操作。");
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
    [Description("列出服务器中的关卡名称和尺寸，可以向用户汇报这些关卡。")]
    public Task MindustryMaps() => CallAsync("maps");

    [XmlFunction(FunctionMode.OneShot)]
    [Description("读取关卡、波次、所属队伍、库存、附近矿点和最近一次任务状态。")]
    public Task MindustryStatus() => CallAsync("status");

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出当前可研究的科技项目，包括内容ID、前置、剩余资源需求和库存。做新手关卡任务或需要解锁新建筑时先调用它。")]
    public Task MindustryTech() => CallAsync("tech");

    [XmlFunction(FunctionMode.Content)]
    [Description("研究并解锁一个科技项目，内容为科技列表中的内容ID，例如 pneumatic-drill。会使用当前核心库存或战役全局库存，扣除资源并验证解锁结果。")]
    public async Task MindustryResearch(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string content = context.FullContent.Trim();
        if (content.Length is < 1 or > 80 || content.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-') == false)
        {
            interactor.Poke("研究格式为科技列表中的内容ID，例如 pneumatic-drill。");
            return;
        }

        await CallAsync($"research?content={Uri.EscapeDataString(content)}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("让己方陪玩单位建造。内容为以空格分隔的 x y 方向 建筑ID，例如 216 84 0 copper-wall。先查询状态，则可获取推荐坐标和建筑列表。建造单位会前往目标，系统会自动报告完工或失败。")]
    public async Task MindustryBuild(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || int.TryParse(parts[0], out int x) == false
            || int.TryParse(parts[1], out int y) == false || int.TryParse(parts[2], out int rotation) == false)
        {
            interactor.Poke("建造格式为 x y 方向 建筑ID，例如 216 84 0 copper-wall。");
            return;
        }

        await CallAsync($"build?x={x}&y={y}&rotation={rotation}&block={Uri.EscapeDataString(parts[3])}", HttpMethod.Post);
    }


    [XmlFunction(FunctionMode.Content)]
    [Description("让陪玩单位自主前往目标格，到达后会自动报告。内容为 x y，例如 220 90。")]
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
    [Description("让陪玩单位自动找矿、采集并运入己方核心，完成后会自动报告。内容为物资ID和数量，例如 copper 10。支持 copper、lead、coal、sand，数量 1 至 99。")]
    public async Task MindustryMine(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("copper" or "lead" or "coal" or "sand") || int.TryParse(parts[1], out int amount) == false
            || amount is < 1 or > 99)
        {
            interactor.Poke("采运格式为物资ID和数量，例如 coal 10。");
            return;
        }

        await CallAsync($"mine?item={parts[0]}&amount={amount}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("取消此单位正在执行的任务，已完成的采运或建造会保留。")]
    public Task MindustryCancel() => CallAsync("cancel", HttpMethod.Post);
    [XmlFunction(FunctionMode.Content)]
    [Description("查看指定位置附近的地形、矿石和建筑网格。内容为中心点 x y 和半径 1 至 6，例如 230 97 3。")]
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

        await CallAsync($"survey?x={x}&y={y}&radius={radius}");
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("仅在用户要求持续自主玩当前对局时启动。内容是获准执行的具体目标。闲置时会收到状态用来选择下一步；目标达成时调用 MindustryAutoplayStop。")]
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
    [Description("停止自动安排后续任务。若还需要中止当前游戏动作，另行调用 MindustryCancel。")]
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
    [Description("让单位自动规划并铺设从同种矿床到己方核心的机械钻头和传送带。内容为 copper、lead、coal 或 sand。同一局再次调用会修复该资源产线的缺损。会等待核心库存增加后自动回报。")]
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
    [Description("让陪玩单位前往指定格守卫，自动瞄准射击，有铜时为附近低弹量双管炮补给，生命过低时撤回。内容为 x y 秒数（5～90）。")]
    public async Task MindustryGuard(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int seconds) == false || seconds is < 5 or > 90)
        {
            interactor.Poke("守卫格式为 x y 秒数（5～90）。");
            return;
        }

        await CallAsync($"guard?x={x}&y={y}&seconds={seconds}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("从己方核心取铜并运到指定的己方双管炮，按炮塔容量补给。内容为炮塔 x y 和物资数量，例如 223 80 10。")]
    public async Task MindustryResupply(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || int.TryParse(parts[2], out int amount) == false || amount is < 1 or > 99)
        {
            interactor.Poke("补给格式为炮塔 x y 铜数量（1～99）。");
            return;
        }

        await CallAsync($"resupply?x={x}&y={y}&amount={amount}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("仅在用户允许提前开波、而且己方防守已就绪时，开始下一波生存敌军。")]
    public Task MindustryNextWave() => CallAsync("wave", HttpMethod.Post);

    [XmlFunction(FunctionMode.Content)]
    [Description("选择一台己方无人操控且能伤害建筑的作战单位，进攻指定格的可见敌方建筑。内容为 x y。生命过低时撤回己方核心。")]
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
    [Description("设置己方陆军工厂生产方案，格式为 x y 兵种ID，例如 350 65 dagger。工厂需要供电和材料才会出兵。")]
    public async Task MindustryFactory(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false)
        {
            interactor.Poke("工厂配置格式为 x y 兵种ID，例如 350 65 dagger。");
            return;
        }

        await CallAsync($"factory?x={x}&y={y}&unit={Uri.EscapeDataString(parts[2])}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("从己方核心给已配置的工厂、硅冶炼炉或燃烧发电机运送材料。格式为 x y 物资ID 数量，例如 349 65 silicon 10。等待送达后再运下一种。")]
    public async Task MindustryFactoryFeed(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string[] parts = context.FullContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || int.TryParse(parts[0], out int x) == false || int.TryParse(parts[1], out int y) == false
            || parts[2] is not ("silicon" or "lead" or "coal" or "titanium" or "sand")
            || int.TryParse(parts[3], out int amount) == false || amount is < 1 or > 99)
        {
            interactor.Poke("工厂供料格式为 x y 物资ID 数量（1～99），支持 silicon、lead、coal、titanium、sand。");
            return;
        }

        await CallAsync($"factory-feed?x={x}&y={y}&item={parts[2]}&amount={amount}", HttpMethod.Post);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("连接己方电力节点和电力建筑，格式为 节点x 节点y 目标x 目标y。重复调用会保留现有连接。")]
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
    [Description("从己方硅冶炼炉取出已制成的硅并运回核心。格式为 x y 数量。")]
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
    CancellationTokenSource? autoplay;
    XmlHandler? operationHandler;

    static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    async Task CallAsync(string path, HttpMethod? method = null)
    {
        if (IsConfigured() == false)
        {
            interactor.Poke("请先在 Mindustry 陪玩模块配置有效的端口、AI 队伍编号和访问令牌。");
            return;
        }

        try
        {
            (HttpStatusCode status, string result, long taskId, long round) = await SendAsync(path, method ?? HttpMethod.Get, DestroyCancellationToken);
            if (taskId != 0)
            {
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

                interactor.Poke($"Mindustry 任务 #{taskId} 回报：{result}");
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
    }

    async Task DriveAutoplayAsync(CancellationTokenSource session, string objective)
    {
        CancellationToken cancellationToken = session.Token;
        try
        {
            long round = 0;
            while (true)
            {
                string path = round == 0 ? "autonomy" : $"autonomy?round={round}";
                (HttpStatusCode status, string result, _, long currentRound) = await SendAsync(path, HttpMethod.Get, cancellationToken);
                if (status == HttpStatusCode.OK)
                    interactor.Poke($"Mindustry 自主陪玩目标：{objective}\n{result}\n空闲时只下达一项任务，等回报再决定下一步。守卫中只决定是否开波。目标达成或无法继续时，停止自主陪玩并说明结果。");
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
