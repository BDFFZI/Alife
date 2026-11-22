package alife.mindustry;

import arc.ApplicationListener;
import arc.Core;
import arc.Events;
import arc.files.Fi;
import arc.util.Log;
import mindustry.Vars;
import mindustry.content.Blocks;
import mindustry.content.Items;
import mindustry.content.TechTree;
import mindustry.content.TechTree.TechNode;
import mindustry.ctype.UnlockableContent;
import mindustry.entities.Units;
import mindustry.entities.units.BuildPlan;
import mindustry.game.EventType.BlockBuildEndEvent;
import mindustry.game.EventType.GameOverEvent;
import mindustry.game.EventType.WorldLoadEvent;
import mindustry.game.EventType.Trigger;
import mindustry.game.Team;
import mindustry.gen.Building;
import mindustry.gen.Groups;
import mindustry.gen.Player;
import mindustry.gen.Unit;
import mindustry.mod.Plugin;
import mindustry.type.Item;
import mindustry.type.ItemStack;
import mindustry.type.Sector;
import mindustry.world.Block;
import mindustry.world.Build;
import mindustry.world.Tile;
import mindustry.world.blocks.units.UnitFactory;
import mindustry.world.blocks.units.UnitFactory.UnitFactoryBuild;
import mindustry.world.blocks.defense.turrets.ItemTurret.ItemTurretBuild;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.SocketException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.PriorityQueue;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.FutureTask;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

public class MindustryBridge extends Plugin {
    private final CompanionTasks companion = new CompanionTasks();
    private Block[] blocks;
    private Team assignedTeam;
    private Team winner;
    private int clearedThrough;
    private long round = System.currentTimeMillis();

    private record Reply(int code, String message, long taskId, long round) {}
    private record FoundObject(int x, int y, String description, float distance) {}

    private static final class BridgeServer implements AutoCloseable {
        private final MindustryBridge bridge;
        private final String token;
        private final ServerSocket socket;
        private final Thread thread;

        BridgeServer(MindustryBridge bridge, int port, String token) throws IOException {
            this.bridge = bridge;
            this.token = token;
            this.socket = new ServerSocket(port, 8, InetAddress.getByName("127.0.0.1"));
            this.thread = new Thread(this::acceptLoop, "alife-mindustry-bridge");
            this.thread.setDaemon(true);
        }

        void start() {
            thread.start();
        }

        private void acceptLoop() {
            while (!socket.isClosed()) {
                try (Socket client = socket.accept()) {
                    bridge.handle(client, token);
                } catch (SocketException exception) {
                    if (!socket.isClosed()) Log.err(exception);
                } catch (IOException exception) {
                    Log.err(exception);
                }
            }
        }

        @Override
        public void close() throws IOException {
            socket.close();
        }
    }

    @Override
    public void init() {
        Fi configFile = Vars.dataDirectory.child("alife-mindustry.properties");
        Map<String, String> config = readConfig(configFile);
        String token = firstNonEmpty(System.getenv("ALIFE_MINDUSTRY_TOKEN"), config.get("token"));
        if (token == null || !token.matches("[A-Za-z0-9_-]{24,128}")) {
            Log.warn("Alife bridge disabled: set a valid token in @", configFile.absolutePath());
            return;
        }
        String port = firstNonEmpty(System.getenv("ALIFE_MINDUSTRY_PORT"), config.get("port"), "18768");
        String teamId = firstNonEmpty(System.getenv("ALIFE_MINDUSTRY_TEAM"), config.get("team"));
        if (teamId != null && !teamId.equals("0")) {
            if (!teamId.matches("[0-9]{1,3}") || Integer.parseInt(teamId) > 255) {
                Log.warn("Alife bridge disabled: ALIFE_MINDUSTRY_TEAM must be a team ID between 0 and 255.");
                return;
            }
            assignedTeam = Team.get(Integer.parseInt(teamId));
        }
        blocks = new Block[]{Blocks.copperWall, Blocks.conveyor, Blocks.mechanicalDrill, Blocks.duo, Blocks.groundFactory, Blocks.powerNode, Blocks.solarPanel, Blocks.siliconSmelter, Blocks.combustionGenerator};
        try {
            BridgeServer server = new BridgeServer(this, Integer.parseInt(port), token);
            server.start();
            Core.app.addListener(new ApplicationListener() {
                @Override
                public void dispose() {
                    try {
                        server.close();
                    } catch (IOException exception) {
                        Log.err(exception);
                    }
                }
            });
            Log.info("Alife Mindustry config file: @", configFile.absolutePath());
            Log.info("Alife Mindustry bridge listening on 127.0.0.1:@", port);
        } catch (IOException | IllegalArgumentException exception) {
            Log.err("Cannot start Alife Mindustry bridge.", exception);
            return;
        }
        Events.on(WorldLoadEvent.class, event -> {
            companion.reset();
            winner = null;
            clearedThrough = 0;
            round++;
        });
        Events.on(GameOverEvent.class, event -> winner = event.winner);
        Events.on(BlockBuildEndEvent.class, event -> {
            if (!event.breaking) companion.constructionFinished(event.unit, event.tile);
        });
        Events.run(Trigger.update, companion::update);
    }

    private static Map<String, String> readConfig(Fi file) {
        Map<String, String> values = new HashMap<>();
        if (file.exists()) {
            for (String line : file.readString().split("\\R")) {
                String value = line.trim();
                if (value.isEmpty() || value.startsWith("#")) continue;
                int separator = value.indexOf('=');
                if (separator <= 0) continue;
                values.put(value.substring(0, separator).trim().toLowerCase(Locale.ROOT), value.substring(separator + 1).trim());
            }
            return values;
        }

        values.put("port", "18768");
        values.put("token", randomToken());
        file.writeString("# Alife Mindustry bridge. Copy these values into the Alife module.\n"
            + "port=" + values.get("port") + "\n"
            + "token=" + values.get("token") + "\n");
        return values;
    }

    private static String randomToken() {
        byte[] bytes = new byte[24];
        new SecureRandom().nextBytes(bytes);
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) result.append(String.format(Locale.ROOT, "%02x", value));
        return result.toString();
    }

    private static String firstNonEmpty(String... values) {
        for (String value : values) {
            if (value != null && !value.isBlank()) return value.trim();
        }
        return null;
    }

    private void handle(Socket client, String token) throws IOException {
        BufferedReader reader = new BufferedReader(new InputStreamReader(client.getInputStream(), StandardCharsets.UTF_8));
        OutputStream output = client.getOutputStream();
        String requestLine = reader.readLine();
        if (requestLine == null || requestLine.isBlank()) {
            respond(output, 400, "无效请求。", 0, 0);
            return;
        }

        Map<String, String> headers = new HashMap<>();
        String line;
        while ((line = reader.readLine()) != null && !line.isEmpty()) {
            int separator = line.indexOf(':');
            if (separator <= 0) continue;
            headers.put(line.substring(0, separator).trim().toLowerCase(Locale.ROOT),
                line.substring(separator + 1).trim());
        }

        String header = headers.get("authorization");
        if (header == null || !MessageDigest.isEqual(header.getBytes(StandardCharsets.UTF_8),
            ("Bearer " + token).getBytes(StandardCharsets.UTF_8))) {
            respond(output, 401, "需要有效的访问令牌。", 0, 0);
            return;
        }

        String[] requestParts = requestLine.split(" ", 3);
        if (requestParts.length < 2) {
            respond(output, 400, "无效请求。", 0, 0);
            return;
        }
        String method = requestParts[0];
        String target = requestParts[1];
        int queryStart = target.indexOf('?');
        String route = queryStart < 0 ? target : target.substring(0, queryStart);
        String query = queryStart < 0 ? null : target.substring(queryStart + 1);
        {
            FutureTask<Reply> gameRequest = new FutureTask<>(() -> dispatchRequest(method, route, query));
            Core.app.post(gameRequest);
            try {
                Reply reply = gameRequest.get(3, TimeUnit.SECONDS);
                respond(output, reply.code(), reply.message(), reply.taskId(), reply.round());
            } catch (TimeoutException exception) {
                gameRequest.cancel(false);
                respond(output, 503, "游戏线程响应超时，请查询状态。", 0, 0);
            } catch (InterruptedException exception) {
                gameRequest.cancel(false);
                Thread.currentThread().interrupt();
                respond(output, 503, "服务正在关闭。", 0, 0);
            } catch (ExecutionException exception) {
                Log.err(exception.getCause());
                respond(output, 500, "游戏侧执行失败，请查阅 Mindustry 服务器日志。", 0, 0);
            }
        }
    }

    private static void respond(OutputStream output, int status, String message, long taskId, long round) throws IOException {
        byte[] bytes = message.getBytes(StandardCharsets.UTF_8);
        StringBuilder headers = new StringBuilder()
            .append("HTTP/1.1 ").append(status).append(' ').append(reason(status)).append("\r\n")
            .append("Content-Type: text/plain; charset=utf-8\r\n")
            .append("Content-Length: ").append(bytes.length).append("\r\n")
            .append("X-Mindustry-Round: ").append(round).append("\r\n")
            .append("Connection: close\r\n");
        if (taskId != 0) headers.append("X-Mindustry-Task: ").append(taskId).append("\r\n");
        headers.append("\r\n");
        output.write(headers.toString().getBytes(StandardCharsets.US_ASCII));
        output.write(bytes);
        output.flush();
    }

    private static String reason(int status) {
        return switch (status) {
            case 200 -> "OK";
            case 202 -> "Accepted";
            case 400 -> "Bad Request";
            case 401 -> "Unauthorized";
            case 404 -> "Not Found";
            case 409 -> "Conflict";
            case 500 -> "Internal Server Error";
            case 503 -> "Service Unavailable";
            default -> "Response";
        };
    }

    private Reply dispatchRequest(String method, String route, String query) {
        long sequence = companion.sequence();
        if (method.equals("GET") && route.equals("/task")) {
            var task = java.util.regex.Pattern.compile("id=([0-9]{1,18})&round=" + round).matcher(query == null ? "" : query);
            if (task.matches()) {
                long id = Long.parseLong(task.group(1));
                if (id == sequence && companion.isRunning()) return new Reply(202, companion.status(), 0, round);
                String completed = companion.completedTask(id);
                if (completed != null) return new Reply(200, completed, 0, round);
            }
            return new Reply(404, "对局已切换或该任务已超出保留范围。", 0, round);
        }
        if (method.equals("GET") && route.equals("/autonomy")) {
            if (query != null && !query.equals("round=" + round)) return new Reply(409, "对局已切换，自主陪玩结束。", 0, round);
            String message = dispatch("GET", "/status", null);
            Team team = assignedTeam == null ? Vars.state.rules.defaultTeam : assignedTeam;
            if (!Vars.state.isGame() || Vars.state.gameOver || winner != null || team.core() == null
                || (Vars.state.rules.pvp && assignedTeam == null)) return new Reply(409, message, 0, round);
            boolean guardingBetweenWaves = companion.canAdvanceWaveWhileGuarding();
            int status = Vars.state.isPaused() || (companion.isRunning() && !guardingBetweenWaves) ? 202 : 200;
            if (guardingBetweenWaves) message += "\n仍在守卫。若用户允许且防守就绪，可在当前守卫任务期间主动开波。";
            return new Reply(status, message, 0, round);
        }
        String message = dispatch(method, route, query);
        return new Reply(200, message, sequence != companion.sequence() ? companion.sequence() : 0, round);
    }

    private String dispatch(String method, String route, String query) {
        if (method.equals("GET") && route.equals("/maps")) {
            StringBuilder result = new StringBuilder("服务端关卡（最多显示 30 张）：");
            int count = 0;
            for (var map : Vars.maps.all()) {
                if (++count > 30) break;
                result.append("\n- ").append(map.plainName()).append(" (").append(map.width).append(" × ").append(map.height).append(")");
            }
            return result.toString();
        }

        if (winner != null) return "对局结束，胜方：" + winner.name + " (" + winner.id + ")。";
        if (!Vars.state.isGame() || Vars.state.gameOver) return "当前没有运行中的对局。";
        if (Vars.state.rules.pvp && assignedTeam == null) {
            return "对抗模式需要服务器明确指定 AI 势力（ALIFE_MINDUSTRY_TEAM）。";
        }
        Team team = assignedTeam == null ? Vars.state.rules.defaultTeam : assignedTeam;
        Building core = team.core();
        if (core == null) return "指定队伍 " + team.id + " 没有核心，请选择有该队伍核心的地图。";

        if (method.equals("GET") && route.equals("/tech")) return researchStatus(team);

        if (method.equals("GET") && route.equals("/status")) {
            StringBuilder result = new StringBuilder("当前关卡：").append(Vars.state.map.plainName())
                .append("，下一波编号：").append(Vars.state.wave).append("，核心：").append(core.tileX()).append(",").append(core.tileY())
                .append("\n资源：");
            team.items().each((item, amount) -> result.append(item.name).append("=").append(amount).append(" "));
            if (Vars.state.rules.waves) {
                result.append("\n本波剩余敌军：").append(Vars.state.enemies);
                result.append(Vars.spawner.isSpawning() ? "（仍在刷怪）"
                    : Vars.state.enemies > 0 ? "（交战中）" : "（当前没有存活敌军）");
                if (!Vars.spawner.isSpawning() && Vars.state.enemies == 0) {
                    clearedThrough = Math.max(clearedThrough, Vars.state.wave - 1);
                }
                result.append("，已确认清场至第 ").append(clearedThrough).append(" 波");
                if (Vars.state.rules.waveTimer && !Vars.spawner.isSpawning() && Vars.state.enemies == 0 && !Vars.state.isPaused()) {
                    result.append("，下一波倒计时：").append(Math.max(0, Math.round(Vars.state.wavetime / arc.util.Time.toSeconds))).append(" 秒");
                }
                result.append("，出怪口（最多 6 个）：");
                int spawnCount = 0;
                for (Tile spawn : Vars.spawner.getSpawns()) {
                    if (Vars.state.rules.fog && !Vars.fogControl.isVisibleTile(team, spawn.x, spawn.y)) continue;
                    if (spawnCount++ == 6) break;
                    result.append(spawn.x).append(",").append(spawn.y).append(" ");
                }
                if (Vars.state.rules.winWave > 0) result.append("，胜利波次阈值：").append(Vars.state.rules.winWave);
            }
            if (Vars.state.rules.objectives != null && Vars.state.rules.objectives.any()) {
                result.append("\n当前关卡任务（最多 8 项）：");
                int objectiveCount = 0;
                for (var objective : Vars.state.rules.objectives) {
                    if (objective.hidden || objectiveCount++ == 8) continue;
                    result.append(objective.isCompleted() ? "\n- [已完成] " : "\n- [进行中] ")
                        .append(objective.text());
                }
            }
            if (Vars.state.isPaused()) result.append("\n对局已暂停，任务将在恢复后继续。");
            result.append("\n核心生命：").append(Math.round(core.healthf() * 100)).append("%");
            result.append("\n队伍：").append(team.name).append(" (").append(team.id).append(")");
            result.append("\n玩家：");
            boolean hasPlayers = false;
            for (Player player : Groups.player) {
                hasPlayers = true;
                result.append("\n- ").append(player.plainName()).append(" / 队伍 ")
                    .append(player.team().name).append(" (").append(player.team().id).append(")");
                if (player.team() == team || Vars.state.rules.fog == false
                    || Vars.fogControl.isVisibleTile(team, player.tileX(), player.tileY())) {
                    result.append(" / 坐标 ").append(player.tileX()).append(",").append(player.tileY());
                    Unit playerUnit = player.unit();
                    result.append(" / ").append(playerUnit == null ? "无单位"
                        : playerUnit.type.name + " 生命 " + Math.round(playerUnit.healthf() * 100) + "%");
                } else {
                    result.append(" / 位置不可见");
                }
            }
            if (hasPlayers == false) result.append("无");
            result.append("\n陪玩单位：").append(companion.unitStatus());
            result.append("\n己方作战单位（最多 8 台）：");
            int combatUnits = 0;
            for (var unit : team.data().units) {
                if (!unit.isValid() || unit.isPlayer()
                    || !unit.type.weapons.contains(weapon -> weapon.bullet.collidesGround
                        && weapon.bullet.buildingDamageMultiplier > 0)) continue;
                result.append(unit.type.name).append("@").append(unit.tileX()).append(",").append(unit.tileY())
                    .append("/").append(Math.round(unit.healthf() * 100)).append("% ");
                if (++combatUnits == 8) break;
            }
            if (combatUnits == 0) result.append("无");
            result.append("\n任务：").append(companion.status());
            result.append("\n就近矿点：");
            for (var material : Vars.indexer.getAllPresentOres()) {
                Tile floorOre = Vars.indexer.findClosestOre(core.x, core.y, material);
                Tile wallOre = Vars.indexer.findClosestWallOre(core.x, core.y, material);
                Tile ore;
                String kind;
                if (floorOre == null) {
                    ore = wallOre;
                    kind = "wall";
                } else if (wallOre == null) {
                    ore = floorOre;
                    kind = "floor";
                } else if (floorOre.dst2(core.x, core.y) <= wallOre.dst2(core.x, core.y)) {
                    ore = floorOre;
                    kind = "floor";
                } else {
                    ore = wallOre;
                    kind = "wall";
                }
                if (ore != null && (!Vars.state.rules.fog || Vars.fogControl.isVisibleTile(team, ore.x, ore.y))) {
                    result.append(material.name).append("@").append(ore.x).append(",").append(ore.y)
                        .append("(").append(kind).append(") ");
                }
            }
            var threat = Units.closestEnemy(team, core.x, core.y, Vars.tilesize * 40,
                enemy -> !Vars.state.rules.fog || Vars.fogControl.isVisible(team, enemy.x, enemy.y));
            result.append("\n核心附近敌军：");
            if (threat == null) result.append("未发现");
            else result.append(threat.type.name).append("@").append(threat.tileX()).append(",").append(threat.tileY());
            result.append("\n己方双管炮（坐标/弹药/射程格）：");
            int defenses = 0;
            for (Building building : team.data().getBuildings(Blocks.duo)) {
                if (defenses++ == 8) break;
                ItemTurretBuild turret = (ItemTurretBuild)building;
                result.append(turret.tileX()).append(",").append(turret.tileY())
                    .append("/").append(Math.round(turret.getAmmoFraction() * 100)).append("%/").append(Math.round(turret.range() / Vars.tilesize)).append(" ");
            }
            result.append("\n可见敌方核心：");
            int targets = 0;
            for (var opponent : Vars.state.teams.getActive()) {
                if (opponent.team == team || opponent.team == Team.derelict) continue;
                for (Building enemyCore : opponent.cores) {
                    if (targets == 8) break;
                    if (Vars.state.rules.fog && !Vars.fogControl.isVisible(team, enemyCore.x, enemyCore.y)) continue;
                    result.append(opponent.team.name).append("@").append(enemyCore.tileX()).append(",")
                        .append(enemyCore.tileY()).append("/").append(Math.round(enemyCore.healthf() * 100)).append("% ");
                    targets++;
                }
            }
            if (targets == 0) result.append("未发现");
            for (Block industry : new Block[]{Blocks.siliconSmelter, Blocks.combustionGenerator}) {
                result.append("\n己方 ").append(industry.name).append("：");
                int count = 0;
                for (Building building : team.data().getBuildings(industry)) {
                    if (count++ == 8) break;
                    result.append(building.tileX()).append(",").append(building.tileY()).append(" ")
                        .append(building.status());
                    if (building instanceof mindustry.world.blocks.power.PowerGenerator.GeneratorBuild generator) {
                        result.append(" 发电=").append(Math.round(generator.getPowerProduction() * 60)).append("/秒 ");
                    } else {
                        result.append(" 供电=").append(Math.round(building.power.status * 100)).append("% ");
                    }
                    building.items.each((material, amount) -> result.append(material.name).append("=").append(amount).append(" "));
                    result.append("；");
                }
            }
            result.append("\n己方陆军工厂：");
            for (Building building : team.data().getBuildings(Blocks.groundFactory)) {
                UnitFactoryBuild factory = (UnitFactoryBuild)building;
                result.append(factory.tileX()).append(",").append(factory.tileY()).append(" ")
                    .append(factory.unit() == null ? "未选择兵种" : factory.unit().name)
                    .append(" 进度=").append(Math.round(factory.fraction() * 100)).append("% ").append(factory.status())
                    .append(" 电力=").append(Math.round(factory.power.status * 100)).append("% ")
                    .append("硅=").append(factory.items.get(Items.silicon)).append(" 铅=")
                    .append(factory.items.get(Items.lead));
                Tile exit = factoryExit(factory.tileX(), factory.tileY(), factory.block, factory.rotation);
                if (exit != null) {
                    result.append(" 出口=").append(exit.x).append(",").append(exit.y);
                    if (exit.solid()) result.append("（被 ").append(exit.block().name).append(" 挡住）");
                }
                result.append("；");
            }
            if (team.data().getBuildings(Blocks.groundFactory).isEmpty()) {
                result.append("\n首台 dagger 所需库存（含陆军工厂，不含供电设施）：");
                var total = new mindustry.world.modules.ItemModule();
                for (var requirement : Blocks.groundFactory.requirements) {
                    total.add(requirement.item, Math.round(requirement.amount * Vars.state.rules.buildCostMultiplier));
                }
                var firstPlan = ((UnitFactory)Blocks.groundFactory).plans.find(plan -> plan.unit == mindustry.content.UnitTypes.dagger);
                if (firstPlan != null && !Vars.state.rules.isBanned(firstPlan.unit)) {
                    for (var requirement : firstPlan.requirements) total.add(requirement.item, Math.round(requirement.amount * Vars.state.rules.unitCost(team)));
                    total.each((material, amount) -> result.append(material.name).append("=")
                        .append(amount).append("（缺 ").append(Math.max(0, amount - team.items().get(material))).append("） "));
                }
            }
            result.append("\n陆军工厂每台所需物资：");
            for (var plan : ((UnitFactory)Blocks.groundFactory).plans) {
                if (Vars.state.rules.isBanned(plan.unit)) continue;
                result.append("\n- ").append(plan.unit.name).append("：");
                for (var requirement : plan.requirements) {
                    result.append(requirement.item.name).append("=").append(Math.round(requirement.amount * Vars.state.rules.unitCost(team))).append(" ");
                }
            }
            result.append("\n支持建筑：");
            for (Block block : blocks) {
                result.append("\n- ").append(block.name).append("（建材：");
                for (var requirement : block.requirements) {
                    result.append(requirement.item.name).append("=")
                        .append(Math.round(requirement.amount * Vars.state.rules.buildCostMultiplier)).append(" ");
                }
                result.append("）");
            }
            var smelter = (mindustry.world.blocks.production.GenericCrafter)Blocks.siliconSmelter;
            for (var consumer : smelter.consumers) {
                if (consumer instanceof mindustry.world.consumers.ConsumeItems ingredients) {
                    result.append("\n硅冶炼每批：");
                    for (var input : ingredients.items) result.append(input.item.name).append("=").append(input.amount).append(" ");
                    result.append("→ ").append(smelter.outputItem.item.name).append("=").append(smelter.outputItem.amount);
                }
            }
            result.append("\n核心附近可建双管炮的坐标（由近到远）：");
            int sites = 0;
            for (int distance = 1; distance <= 8 && sites < 8; distance++) {
                for (int dx = -distance; dx <= distance && sites < 8; dx++) {
                    int dy = distance - Math.abs(dx);
                    for (int sign = -1; sign <= 1 && sites < 8; sign += 2) {
                        int x = core.tileX() + dx, y = core.tileY() + dy * sign;
                        if (dy == 0 && sign == 1) continue;
                        Tile tile = Vars.world.tile(x, y);
                        if (tile == null || tile.block() != Blocks.air) continue;
                        if (Vars.state.rules.fog && !Vars.fogControl.isVisibleTile(team, x, y)) continue;
                        if (Build.validPlace(Blocks.duo, team, x, y, 0)) {
                            result.append(x).append(",").append(y).append(" ");
                            sites++;
                        }
                    }
                }
            }
            return result.toString();
        }

        if (method.equals("GET") && route.equals("/survey")) return survey(team, query);
        if (method.equals("GET") && route.equals("/find")) return findVisibleObjects(team, core, query);
        if (!method.equals("POST")) return "未知操作。";
        if (route.equals("/cancel")) return companion.cancel();
        if (route.equals("/research")) {
            var research = java.util.regex.Pattern.compile("content=([a-z0-9-]{1,80})").matcher(query == null ? "" : query);
            return research.matches() ? research(team, research.group(1)) : "研究格式为 content=建筑或单位ID。";
        }
        if (route.equals("/wave")) {
            if (Vars.state.rules.pvp || !Vars.state.rules.waves || !Vars.state.rules.waveSending) {
                return "当前模式不允许主动开始波次。";
            }
            if (Vars.state.isPaused() || Vars.state.enemies > 0 || Vars.spawner.isSpawning()) {
                return "当前暂停或上一波尚未清理，无法开始下一波。";
            }
            int wave = Vars.state.wave;
            Vars.logic.runWave();
            return "已开始第 " + wave + " 波。";
        }
        if (query == null || query.length() > 160) return "缺少任务参数。";
        if (route.equals("/move")) {
            var position = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})").matcher(query);
            if (!position.matches()) return "无效移动坐标。";
            Tile target = Vars.world.tile(Integer.parseInt(position.group(1)), Integer.parseInt(position.group(2)));
            return target == null ? "目标不在地图内。" : companion.move(core, target);
        }
        if (route.equals("/collect-silicon")) {
            var source = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&amount=([1-9][0-9]?)").matcher(query);
            if (!source.matches()) return "无效回收参数。";
            Building target = Vars.world.build(Integer.parseInt(source.group(1)), Integer.parseInt(source.group(2)));
            if (target == null || target.team != team || target.block != Blocks.siliconSmelter) return "需要己方硅冶炼炉。";
            return companion.collectSilicon(core, target, Integer.parseInt(source.group(3)));
        }
        if (route.equals("/factory-feed")) {
            var supply = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&item=(silicon|lead|coal|titanium|sand)&amount=([1-9][0-9]?)").matcher(query);
            if (!supply.matches()) return "无效工厂供料参数。";
            Building target = Vars.world.build(Integer.parseInt(supply.group(1)), Integer.parseInt(supply.group(2)));
            if (target == null || target.team != team || !(target instanceof UnitFactoryBuild || target.block == Blocks.siliconSmelter || target.block == Blocks.combustionGenerator)) {
                return "需要己方工厂、硅冶炼炉或燃烧发电机。";
            }
            var material = Vars.content.items().find(item -> item.name.equals(supply.group(3)));
            if (material == null || !target.acceptItem(core, material)) return "当前生产方案不需要该物资，或库存已满。";
            int capacity = target.getMaximumAccepted(material) - target.items.get(material);
            return companion.resupply(core, target, material, Math.min(capacity, Integer.parseInt(supply.group(4))));
        }
        if (route.equals("/power-link")) {
            var link = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&toX=([0-9]{1,5})&toY=([0-9]{1,5})").matcher(query);
            if (!link.matches()) return "无效电网连接参数。";
            Building from = Vars.world.build(Integer.parseInt(link.group(1)), Integer.parseInt(link.group(2)));
            Building to = Vars.world.build(Integer.parseInt(link.group(3)), Integer.parseInt(link.group(4)));
            if (from == null || to == null || from == to || from.team != team || to.team != team
                || !(from.block instanceof mindustry.world.blocks.power.PowerNode node) || to.power == null) {
                return "只能连接己方电力节点和己方电力建筑。";
            }
            if (from.power.links.contains(to.pos())) return "该电力连接已存在。";
            if (!node.linkValid(from, to)) return "目标超出节点范围、被绝缘墙阻挡或节点已满。";
            from.configured(null, to.pos());
            return from.power.links.contains(to.pos()) ? "电力连接已建立。" : "游戏拒绝了该电力连接。";
        }
        if (route.equals("/factory")) {
            var selection = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&unit=([a-z-]+)").matcher(query);
            if (!selection.matches()) return "无效工厂参数。";
            Building target = Vars.world.build(Integer.parseInt(selection.group(1)), Integer.parseInt(selection.group(2)));
            if (!(target instanceof UnitFactoryBuild factory) || target.team != team || target.block != Blocks.groundFactory) {
                return "只能配置己方陆军工厂。";
            }
            UnitFactory block = (UnitFactory)factory.block;
            int plan = block.plans.indexOf(candidate -> candidate.unit.name.equals(selection.group(3)));
            if (plan < 0 || Vars.state.rules.isBanned(block.plans.get(plan).unit)) return "该工厂无法生产此兵种。";
            factory.configured(null, plan);
            if (factory.currentPlan != plan) return "游戏拒绝了该工厂配置。";
            return "已选择 " + factory.unit().name + "，请保证供电和生产材料。";
        }
        if (route.equals("/attack")) {
            var position = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})").matcher(query);
            if (!position.matches()) return "无效进攻坐标。";
            int x = Integer.parseInt(position.group(1)), y = Integer.parseInt(position.group(2));
            Building target = Vars.world.build(x, y);
            if (target == null || target.team == team || target.team == Team.derelict
                || (Vars.state.rules.fog && !Vars.fogControl.isVisibleTile(team, x, y))) {
                return "进攻需要己方可见的敌方建筑。";
            }
            return companion.attack(core, target);
        }
        if (route.equals("/resupply")) {
            var ammo = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&amount=([0-9]{1,2})").matcher(query);
            if (!ammo.matches() || Integer.parseInt(ammo.group(3)) == 0) return "无效炮塔补给参数。";
            Building target = Vars.world.build(Integer.parseInt(ammo.group(1)), Integer.parseInt(ammo.group(2)));
            if (!(target instanceof ItemTurretBuild) || target.team != team || target.block != Blocks.duo) {
                return "只能为己方双管炮补给铜。";
            }
            return companion.resupply(core, target, Items.copper, Integer.parseInt(ammo.group(3)));
        }
        if (route.equals("/guard")) {
            var guard = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&seconds=([0-9]{1,3})").matcher(query);
            if (!guard.matches()) return "无效守卫参数。";
            Tile target = Vars.world.tile(Integer.parseInt(guard.group(1)), Integer.parseInt(guard.group(2)));
            int seconds = Integer.parseInt(guard.group(3));
            if (target == null || seconds < 5 || seconds > 90) return "守卫需要有效坐标和 5～90 秒的时间。";
            return companion.guard(core, target, seconds);
        }
        if (route.equals("/mine")) {
            var mining = java.util.regex.Pattern.compile("item=([a-z0-9-]{1,80})&amount=([0-9]{1,2})").matcher(query);
            if (!mining.matches() || Integer.parseInt(mining.group(2)) == 0) return "采运任务需要有效的物资ID和 1～99 的数量。";
            Item item = Vars.content.item(mining.group(1));
            if (item == null) return "找不到物资：" + mining.group(1) + "。";
            return companion.mine(core, item, Integer.parseInt(mining.group(2)));
        }
        if (route.equals("/supply")) {
            if (!query.matches("item=(copper|lead|coal|sand)")) return "产线支持铜、铅、煤和沙。";
            var item = Vars.content.items().find(material -> material.name.equals(query.substring(5)));
            var plans = companion.knowsSupply(item) ? companion.damagedSupply(core, item) : SupplyRoute.plan(core, item);
            if (companion.knowsSupply(item) && plans.isEmpty()) return "已记录的产线建筑均在原位。";
            for (BuildPlan plan : plans) {
                Tile tile = Vars.world.tile(plan.x, plan.y);
                boolean canRestore = tile != null && (tile.block() == Blocks.air || (tile.team() == team && tile.block() == plan.block))
                    && (!Vars.state.rules.fog || Vars.fogControl.isVisibleTile(team, plan.x, plan.y));
                if (!canRestore || !Build.validPlace(plan.block, team, plan.x, plan.y, plan.rotation)) {
                    return "产线格不可见、被占用或无法施工：" + plan.x + "," + plan.y;
                }
            }
            if (!Vars.state.rules.isInfiniteResources(team)) {
                var required = new mindustry.world.modules.ItemModule();
                for (BuildPlan plan : plans) {
                    for (var stack : plan.block.requirements) {
                        required.add(stack.item, Math.round(stack.amount * Vars.state.rules.buildCostMultiplier));
                    }
                }
                boolean[] insufficient = {false};
                required.each((material, quantity) -> insufficient[0] |= !team.items().has(material, quantity));
                if (insufficient[0]) return "核心资源不足，无法完成整条产线。";
            }
            return companion.supply(core, item, plans);
        }
        if (!route.equals("/build")) return "未知操作。";
        var parameters = java.util.regex.Pattern.compile("x=(\\d+)&y=(\\d+)&rotation=([0-3])&block=([a-z-]+)").matcher(query);
        if (!parameters.matches() || parameters.group(1).length() > 5 || parameters.group(2).length() > 5) {
            return "无效建造参数。";
        }
        int x = Integer.parseInt(parameters.group(1));
        int y = Integer.parseInt(parameters.group(2));
        int rotation = Integer.parseInt(parameters.group(3));
        String name = parameters.group(4);
        Block block = Vars.content.block(name);
        if (block == null || block == Blocks.air) return "找不到建筑：" + name;
        if (Vars.state.isCampaign() && !block.unlockedHost()) return "建筑尚未解锁：" + name + "。先查询科技树并研究。";
        if (block.isHidden() || Vars.state.rules.isBanned(block)) return "当前规则不允许建造：" + name;
        return build(core, block, x, y, rotation);
    }

    private String researchStatus(Team team) {
        Sector currentSector = Vars.state.isCampaign() ? Vars.state.rules.sector : null;
        TechNode techRoot = currentTechRoot(currentSector);
        StringBuilder result = new StringBuilder("当前可研究项目（需求为剩余量）：");
        int count = 0;
        for (TechNode node : TechTree.all) {
            UnlockableContent content = node.content;
            if (content == null) continue;
            if (!content.locked() || !objectivesComplete(node)) continue;
            if (techRoot != null && node.rootNode != techRoot) continue;
            if (node != preferredTechNode(content, techRoot)) continue;
            if (count++ == 20) break;
            result.append("\n- ").append(content.name).append("（").append(content.localizedName).append("）");
            result.append(content.unlockedHost() ? "：已解锁" : "：未解锁");
            if (node.parent != null) result.append("；前置=").append(node.parent.content.name);
            result.append("；需求=").append(researchCost(node, team));
            if (!objectivesComplete(node)) result.append("；还有未完成目标");
        }
        if (count == 0) result.append("\n当前没有可直接研究的新项目。");
        else if (count > 20) result.append("\n仅显示前 20 项。");
        result.append(currentSector == null
            ? "\n研究资源来源：当前核心库存。"
            : "\n研究资源来源：本星区已有基地的全局库存。");
        return result.toString();
    }

    private String research(Team team, String name) {
        if (Vars.net != null && Vars.net.client()) {
            return "当前客户端连接的房间由主机控制解锁状态，不能直接研究。请在单人游戏或本地主机房间使用。";
        }
        UnlockableContent content = Vars.content.byName(name) instanceof UnlockableContent value ? value : null;
        if (content == null) return "找不到可研究内容：" + name;
        Sector currentSector = Vars.state.isCampaign() ? Vars.state.rules.sector : null;
        TechNode node = preferredTechNode(content, currentTechRoot(currentSector));
        if (node == null) return name + " 不在当前星球的科技树中。";
        if (content.unlockedHost()) return content.name + "（" + content.localizedName + "）已经解锁。";
        if (!objectivesComplete(node)) {
            StringBuilder blockers = new StringBuilder();
            for (var objective : node.objectives) {
                if (objective.complete()) continue;
                if (blockers.length() > 0) blockers.append("、");
                blockers.append(objective.display());
            }
            return "前置目标尚未完成：" + blockers + "。";
        }

        for (ItemStack requirement : node.requirements) {
            ItemStack finished = matchingRequirement(node, requirement.item);
            int missing = requirement.amount - finished.amount;
            if (missing <= 0) continue;
            finished.amount += takeResearchItem(team, currentSector, requirement.item, missing);
        }
        node.save();

        boolean complete = true;
        for (ItemStack requirement : node.requirements) {
            if (matchingRequirement(node, requirement.item).amount < requirement.amount) {
                complete = false;
                break;
            }
        }
        if (!complete) {
            return "已提交现有资源，研究未完成。剩余：" + researchCost(node, team)
                + "。补充资源后再次研究 " + content.name + "。";
        }

        TechNode unlockNode = node;
        while (unlockNode != null) {
            unlockNode.content.unlock();
            unlockNode = unlockNode.parent;
        }
        arc.Events.fire(new mindustry.game.EventType.ResearchEvent(content));
        return "研究完成：" + content.name + "（" + content.localizedName + "）已解锁，现在可以建造或使用。";
    }

    private TechNode currentTechRoot(Sector currentSector) {
        if (currentSector != null) return currentSector.planet.techTree;
        mindustry.type.Planet planet = Vars.state.getPlanet();
        return planet == null ? null : planet.techTree;
    }

    private TechNode preferredTechNode(UnlockableContent content, TechNode root) {
        TechNode fallback = null;
        for (TechNode candidate : content.techNodes) {
            if (fallback == null) fallback = candidate;
            if (root != null && candidate.rootNode == root) return candidate;
        }
        return fallback;
    }

    private boolean objectivesComplete(TechNode node) {
        for (var objective : node.objectives) {
            if (!objective.complete()) return false;
        }
        return true;
    }

    private ItemStack matchingRequirement(TechNode node, Item item) {
        for (int index = 0; index < node.requirements.length; index++) {
            if (node.requirements[index].item == item) return node.finishedRequirements[index];
        }
        throw new IllegalArgumentException("Tech node requirement order changed.");
    }

    private String researchCost(TechNode node, Team team) {
        Sector currentSector = Vars.state.isCampaign() ? Vars.state.rules.sector : null;
        StringBuilder cost = new StringBuilder();
        for (int index = 0; index < node.requirements.length; index++) {
            ItemStack requirement = node.requirements[index];
            int remaining = Math.max(0, requirement.amount - node.finishedRequirements[index].amount);
            if (cost.length() > 0) cost.append(" ");
            cost.append(requirement.item.name).append(" ").append(remaining)
                .append("/库存").append(researchItemCount(team, currentSector, requirement.item));
        }
        return cost.length() == 0 ? "免费" : cost.toString();
    }

    private int researchItemCount(Team team, Sector currentSector, Item item) {
        if (currentSector == null) return team.items().get(item);
        int total = 0;
        for (Sector sector : currentSector.planet.sectors) {
            if (sector.hasBase() && !sector.isFrozen()) total += sector.items().get(item);
        }
        return total;
    }

    private int takeResearchItem(Team team, Sector currentSector, Item item, int requested) {
        if (currentSector == null) {
            int taken = Math.min(requested, team.items().get(item));
            if (taken > 0) team.items().remove(item, taken);
            return taken;
        }

        int remaining = requested;
        int taken = takeSectorItem(currentSector, item, remaining);
        remaining -= taken;
        if (remaining > 0) {
            for (Sector sector : currentSector.planet.sectors) {
                if (sector == currentSector || !sector.hasBase() || sector.isFrozen()) continue;
                int fromSector = takeSectorItem(sector, item, remaining);
                taken += fromSector;
                remaining -= fromSector;
                if (remaining == 0) break;
            }
        }
        return taken;
    }

    private int takeSectorItem(Sector sector, Item item, int requested) {
        int taken = Math.min(requested, sector.items().get(item));
        if (taken > 0) sector.removeItem(item, taken);
        return taken;
    }

    private String survey(Team team, String query) {
        if (query == null || query.length() > 40) return "缺少勘查参数。";
        var coordinates = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&radius=([1-6])").matcher(query);
        if (!coordinates.matches()) return "无效的勘查坐标或半径。";
        int x = Integer.parseInt(coordinates.group(1));
        int y = Integer.parseInt(coordinates.group(2));
        int radius = Integer.parseInt(coordinates.group(3));
        if (Vars.world.tile(x, y) == null) return "中心点不在地图内。";

        StringBuilder result = new StringBuilder("网格按 y 从大到小排列，行内 x 从小到大：");
        for (int row = y + radius; row >= y - radius; row--) {
            result.append("\ny=").append(row);
            for (int column = x - radius; column <= x + radius; column++) {
                result.append(" ");
                Tile tile = Vars.world.tile(column, row);
                if (tile == null || (Vars.state.rules.fog && !Vars.fogControl.isVisibleTile(team, column, row))) {
                    result.append("?");
                } else if (tile.build != null) {
                    result.append(tile.team() == team ? "own:" : "team" + tile.team().id + ":")
                        .append(tile.block().name);
                } else {
                    Item wallDrop = tile.wallDrop();
                    Item drop = tile.drop();
                    Block block = tile.block();
                    if (wallDrop != null) {
                        result.append("wall-ore:").append(wallDrop.name);
                    } else if (drop != null) {
                        result.append("ore:").append(drop.name);
                    } else if (block.isAir() == false) {
                        result.append(block.isStatic() ? "block:" : "solid:").append(block.name);
                    } else if (tile.floor().liquidDrop != null) {
                        result.append("liquid:").append(tile.floor().liquidDrop.name);
                    } else if (tile.floor().hasSurface() == false) {
                        result.append("floor:").append(tile.floor().name);
                    } else {
                        result.append(".");
                    }
                }
            }
        }
        return result.append("\n起始 x=").append(x - radius)
            .append("。ore=地表矿，wall-ore=墙体矿，block/solid=实体方块，liquid=液体，floor=无地表，own/teamN=建筑，?=地图外或不可见。").toString();
    }

    private String findVisibleObjects(Team team, Building core, String query) {
        if (query == null || query.length() > 160) return "缺少搜索参数。";
        var parameters = java.util.regex.Pattern.compile("kind=(ore|block|unit)&id=([a-z0-9-]{1,80})").matcher(query);
        if (!parameters.matches()) return "无效的搜索参数。";

        String kind = parameters.group(1);
        String id = parameters.group(2);
        Item oreItem = null;
        Block targetBlock = null;
        mindustry.type.UnitType targetUnit = null;
        if (kind.equals("ore")) {
            oreItem = Vars.content.item(id);
            if (oreItem == null) return "找不到物资：" + id + "。";
        } else if (kind.equals("block")) {
            targetBlock = Vars.content.block(id);
            if (targetBlock == null) return "找不到方块：" + id + "。";
        } else {
            targetUnit = Vars.content.unit(id);
            if (targetUnit == null) return "找不到单位：" + id + "。";
        }

        PriorityQueue<FoundObject> nearest = new PriorityQueue<>(Comparator.comparingDouble(FoundObject::distance).reversed());
        int[] matches = {0};
        if (kind.equals("ore")) {
            for (int y = 0; y < Vars.world.height(); y++) {
                for (int x = 0; x < Vars.world.width(); x++) {
                    Tile tile = Vars.world.tile(x, y);
                    if (tile == null || !visible(team, x, y)) continue;
                    Item wallDrop = tile.wallDrop();
                    Item drop = tile.drop();
                    if (wallDrop != oreItem && drop != oreItem) continue;
                    keepNearest(nearest, matches, new FoundObject(tile.x, tile.y,
                        wallDrop == oreItem ? "wall" : "floor", tile.dst2(core.x, core.y)));
                }
            }
        } else if (kind.equals("block")) {
            for (int y = 0; y < Vars.world.height(); y++) {
                for (int x = 0; x < Vars.world.width(); x++) {
                    Tile tile = Vars.world.tile(x, y);
                    if (tile == null || !visible(team, x, y)) continue;
                    Building building = tile.build;
                    if (building != null) {
                        if (building.tileX() != x || building.tileY() != y || building.block != targetBlock) continue;
                        keepNearest(nearest, matches, new FoundObject(x, y,
                            (building.team == team ? "own:" : "team" + building.team.id + ":") + targetBlock.name
                                + " 生命=" + Math.round(building.healthf() * 100) + "%",
                            building.dst2(core.x, core.y)));
                    } else if (tile.block() == targetBlock) {
                        keepNearest(nearest, matches, new FoundObject(x, y,
                            tile.block().isStatic() ? "block:" + targetBlock.name : "solid:" + targetBlock.name,
                            tile.dst2(core.x, core.y)));
                    }
                }
            }
        } else {
            for (Unit unit : Groups.unit) {
                if (!unit.isValid() || unit.type != targetUnit || !visible(team, unit.x, unit.y)) continue;
                keepNearest(nearest, matches, new FoundObject(unit.tileX(), unit.tileY(),
                    (unit.team == team ? "own:" : "team" + unit.team.id + ":") + targetUnit.name
                        + " 生命=" + Math.round(unit.healthf() * 100) + "%",
                    unit.dst2(core.x, core.y)));
            }
        }

        if (matches[0] == 0) return "全图没有找到可见目标：" + kind + " " + id + "。";
        List<FoundObject> results = new ArrayList<>(nearest);
        results.sort(Comparator.comparingDouble(FoundObject::distance));
        StringBuilder result = new StringBuilder("全图可见目标共 ").append(matches[0])
            .append(" 个，按离核心由近到远最多显示 50 个：");
        for (FoundObject found : results) {
            result.append("\n- ").append(found.x).append(",").append(found.y).append(" ").append(found.description);
        }
        return result.toString();
    }

    private boolean visible(Team team, float x, float y) {
        return Vars.state.rules.fog == false || Vars.fogControl.isVisible(team, x, y);
    }

    private static void keepNearest(PriorityQueue<FoundObject> nearest, int[] matches, FoundObject found) {
        matches[0]++;
        nearest.offer(found);
        if (nearest.size() > 50) nearest.poll();
    }

    private Tile factoryExit(int x, int y, Block block, int rotation) {
        var direction = arc.math.geom.Geometry.d4(rotation);
        int distance = block.size / 2 + 1;
        return Vars.world.tile(x + direction.x * distance, y + direction.y * distance);
    }

    private String build(Building core, Block block, int x, int y, int rotation) {
        Team team = core.team;
        Tile tile = Vars.world.tile(x, y);
        if (tile == null || tile.block() != Blocks.air || !Build.validPlace(block, team, x, y, rotation)) {
            return "该位置无法建造 " + block.name + "。";
        }
        if (block == Blocks.groundFactory) {
            Tile exit = factoryExit(x, y, block, rotation);
            if (exit == null || exit.solid()) return "陆军工厂需要朝向空旷的出兵口。";
        }
        if (block.solid) {
            var footprint = tile.getLinkedTilesAs(block, new arc.struct.Seq<Tile>());
            for (Building factory : team.data().getBuildings(Blocks.groundFactory)) {
                Tile exit = factoryExit(factory.tileX(), factory.tileY(), factory.block, factory.rotation);
                if (exit != null && footprint.contains(exit, true)) {
                    return "请保留陆军工厂的出兵口：" + exit.x + "," + exit.y + "。";
                }
            }
        }
        if (!Vars.state.rules.isInfiniteResources(team)
            && !team.items().has(block.requirements, Vars.state.rules.buildCostMultiplier)) {
            return "核心资源不足，无法建造 " + block.name + "。";
        }
        return companion.build(core, new BuildPlan(x, y, rotation, block));
    }
}
