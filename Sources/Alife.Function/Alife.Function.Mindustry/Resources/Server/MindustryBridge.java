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
import mindustry.game.MapObjectives.BuildCountObjective;
import mindustry.game.MapObjectives.CommandModeObjective;
import mindustry.game.MapObjectives.CoreItemObjective;
import mindustry.game.MapObjectives.DestroyBlockObjective;
import mindustry.game.MapObjectives.DestroyBlocksObjective;
import mindustry.game.MapObjectives.DestroyUnitsObjective;
import mindustry.game.MapObjectives.FlagObjective;
import mindustry.game.MapObjectives.ItemObjective;
import mindustry.game.MapObjectives.MapObjective;
import mindustry.game.MapObjectives.ResearchObjective;
import mindustry.game.MapObjectives.TimerObjective;
import mindustry.game.MapObjectives.UnitCountObjective;
import mindustry.game.Team;
import mindustry.gen.Building;
import mindustry.gen.Call;
import mindustry.gen.Groups;
import mindustry.gen.Player;
import mindustry.gen.Unit;
import mindustry.mod.Plugin;
import mindustry.type.Item;
import mindustry.type.ItemStack;
import mindustry.type.Planet;
import mindustry.type.Sector;
import mindustry.world.Block;
import mindustry.world.Build;
import mindustry.world.Tile;
import mindustry.world.blocks.environment.Floor;
import mindustry.world.blocks.environment.SteamVent;
import mindustry.world.blocks.power.BeamNode;
import mindustry.world.blocks.power.BeamNode.BeamNodeBuild;
import mindustry.world.blocks.power.PowerNode;
import mindustry.world.blocks.production.Drill;
import mindustry.world.blocks.production.GenericCrafter;
import mindustry.world.blocks.units.UnitFactory;
import mindustry.world.blocks.units.UnitFactory.UnitFactoryBuild;
import mindustry.world.blocks.defense.turrets.ItemTurret;
import mindustry.world.blocks.defense.turrets.ItemTurret.ItemTurretBuild;
import mindustry.world.blocks.distribution.Conveyor;
import mindustry.world.blocks.distribution.Duct;
import mindustry.world.consumers.Consume;
import mindustry.world.consumers.ConsumeItems;

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
import java.util.LinkedHashMap;
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
                    try {
                        bridge.handle(client, token);
                    } catch (SocketException ignored) {
                        // The client can close before its response is written.
                    }
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
            if (winner != null) return new Reply(409, "对局结束，胜方：" + winner.name + " (" + winner.id + ")。", 0, round);
            if (!Vars.state.isGame() || Vars.state.gameOver) return new Reply(409, "当前没有运行中的对局。", 0, round);
            if (Vars.state.rules.pvp && assignedTeam == null) {
                return new Reply(409, "对抗模式需要服务器明确指定 AI 势力（ALIFE_MINDUSTRY_TEAM）。", 0, round);
            }
            Team team = assignedTeam == null ? Vars.state.rules.defaultTeam : assignedTeam;
            Building core = team.core();
            if (core == null) return new Reply(409, "指定队伍 " + team.id + " 没有核心，请选择有该队伍核心的地图。", 0, round);
            String message = compactStatus(team, core);
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
        if (companion.isRunning() && method.equals("GET") == false && route.equals("/cancel") == false) {
            return companion.busy();
        }

        if (method.equals("GET") && route.equals("/tech")) return researchStatus(team);

        if (method.equals("GET") && route.equals("/status")) {
            Planet planet = Vars.state.getPlanet();
            StringBuilder result = new StringBuilder("当前关卡：").append(Vars.state.map.plainName())
                .append("，下一波编号：").append(Vars.state.wave).append("，核心：").append(core.tileX()).append(",").append(core.tileY())
                .append("\n星球：").append(planet == null ? "未知" : planet.localizedName + " (" + planet.name + ")")
                .append("，地图 ").append(Vars.world.width()).append("×").append(Vars.world.height())
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
                    if (spawnCount++ == 6) break;
                    result.append(spawn.x).append(",").append(spawn.y).append(" ");
                }
                if (Vars.state.rules.winWave > 0) result.append("，胜利波次阈值：").append(Vars.state.rules.winWave);
            }
            appendObjectives(result, team, 8);
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
                if (!unit.isValid() || unit.isPlayer() || unit == companion.worker()
                    || !unit.type.weapons.contains(weapon -> weapon.aiControllable && !weapon.noAttack
                        && weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0)) continue;
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
                if (ore != null) {
                    result.append(material.name).append("@").append(ore.x).append(",").append(ore.y)
                        .append("(").append(kind).append(") ");
                }
            }
            result.append("\n采矿：MindustryMine <物资ID> <数量>；更多矿点用 MindustryFind ore <ID>。");
            result.append("\n全图喷口：");
            appendClosestBlocks(result, team, core, "vent", 5);
            var threat = Units.closestEnemy(team, core.x, core.y, Vars.tilesize * 40,
                enemy -> !Vars.state.rules.fog || Vars.fogControl.isVisible(team, enemy.x, enemy.y));
            result.append("\n核心附近敌军：");
            if (threat == null) result.append("未发现");
            else result.append(threat.type.name).append("@").append(threat.tileX()).append(",").append(threat.tileY());
            result.append("\n炮塔（名称@坐标/弹药/射程）：");
            int defenses = 0;
            for (Building building : team.data().buildings) {
                if (!(building instanceof ItemTurretBuild turret)) continue;
                if (defenses++ == 8) break;
                result.append(turret.block.name).append("@").append(turret.tileX()).append(",").append(turret.tileY())
                    .append("/").append(Math.round(turret.getAmmoFraction() * 100)).append("%/")
                    .append(Math.round(turret.range() / Vars.tilesize)).append("格，可装[")
                    .append(ammoTypes(turret)).append("] ");
            }
            if (defenses == 0) result.append("无");
            result.append("\n敌方核心：");
            int targets = 0;
            for (var opponent : Vars.state.teams.getActive()) {
                if (opponent.team == team || opponent.team == Team.derelict) continue;
                for (Building enemyCore : opponent.cores) {
                    if (targets == 8) break;
                    result.append(opponent.team.name).append("@").append(enemyCore.tileX()).append(",")
                        .append(enemyCore.tileY()).append("/").append(Math.round(enemyCore.healthf() * 100)).append("% ");
                    targets++;
                }
            }
            if (targets == 0) result.append("未发现");
            result.append("\n生产与供电：");
            appendIndustrySummary(result, team, 12);
            result.append("\n可建建筑（格式：x y 方向 建筑ID）：");
            appendAvailableBlocks(result, team, planet, core);
            return result.toString();
        }

        if (method.equals("GET") && route.equals("/survey")) return survey(team, query);
        if (method.equals("GET") && route.equals("/find")) return findObjects(team, core, query);
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
            if (target == null || target.team != team || target.items.get(Items.silicon) <= 0) return "该己方建筑当前没有可回收的硅。";
            return companion.collectSilicon(core, target, Integer.parseInt(source.group(3)));
        }
        if (route.equals("/factory-feed")) {
            var supply = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&item=([a-z0-9-]{1,80})&amount=([1-9][0-9]?)").matcher(query);
            if (!supply.matches()) return "无效工厂供料参数。";
            Building target = Vars.world.build(Integer.parseInt(supply.group(1)), Integer.parseInt(supply.group(2)));
            var material = Vars.content.items().find(item -> item.name.equals(supply.group(3)));
            if (target == null || material == null || target.team != team) return "目标建筑或物资不存在。";
            if (!acceptsSupply(target, material)) return "目标建筑不接收该物资。";
            int amount = Math.min(target.getMaximumAccepted(material), Integer.parseInt(supply.group(4)));
            return companion.resupply(core, target, material, amount);
        }
        if (route.equals("/transfer")) {
            var transfer = java.util.regex.Pattern.compile(
                "fromX=([0-9]{1,5})&fromY=([0-9]{1,5})&toX=([0-9]{1,5})&toY=([0-9]{1,5})"
                    + "&item=([a-z0-9-]{1,80})&amount=([1-9][0-9]?)").matcher(query);
            if (!transfer.matches()) return "无效建筑转运参数。";
            Building source = Vars.world.build(Integer.parseInt(transfer.group(1)), Integer.parseInt(transfer.group(2)));
            Building target = Vars.world.build(Integer.parseInt(transfer.group(3)), Integer.parseInt(transfer.group(4)));
            var material = Vars.content.items().find(item -> item.name.equals(transfer.group(5)));
            if (source == null || target == null || material == null
                || source.team != team || target.team != team) {
                return "转运源建筑、目标建筑或物资不存在。";
            }
            return companion.transfer(core, source, target, material, Integer.parseInt(transfer.group(6)));
        }
        if (route.equals("/power-link")) {
            var link = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&toX=([0-9]{1,5})&toY=([0-9]{1,5})").matcher(query);
            if (!link.matches()) return "无效电网连接参数。";
            Building from = Vars.world.build(Integer.parseInt(link.group(1)), Integer.parseInt(link.group(2)));
            Building to = Vars.world.build(Integer.parseInt(link.group(3)), Integer.parseInt(link.group(4)));
            if (from == null) return "起点格没有建筑。";
            if (to == null) return "目标格没有建筑。";
            if (from == to) return "起点和目标不能是同一座建筑。";
            if (from.team != team || to.team != team) return "只能连接己方建筑。";
            if (from.block instanceof BeamNode beam && from instanceof BeamNodeBuild build)
                return connectBeamNode(build, beam, to);
            if (!(from.block instanceof PowerNode node)) {
                return "起点不是电力节点。埃里克尔 beam-node 不需要手动连线，请通过同轴摆放触发自动连接。";
            }
            if (to.power == null) return "目标建筑不可接电。";
            if (from.power.links.contains(to.pos())) return "该电力连接已存在。";
            if (!node.linkValid(from, to)) return "目标超出节点范围、被绝缘墙阻挡或节点已满。";
            from.configured(null, to.pos());
            return from.power.links.contains(to.pos()) ? "电力连接已建立。" : "游戏拒绝了该电力连接。";
        }
        if (route.equals("/deconstruct")) {
            var target = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})").matcher(query);
            if (!target.matches()) return "无效拆除坐标。";
            Tile tile = Vars.world.tile(Integer.parseInt(target.group(1)), Integer.parseInt(target.group(2)));
            if (tile == null || tile.build == null || tile.build.team != team) return "该格没有可拆除的己方建筑。";
            if (tile.build instanceof mindustry.world.blocks.storage.CoreBlock.CoreBuild) return "不能拆除己方核心。";
            Block block = tile.block();
            companion.cancel();
            Call.deconstructFinish(tile, block, null);
            return "已拆除 " + block.name + "@" + tile.x + "," + tile.y + "。";
        }
        if (route.equals("/factory")) {
            var selection = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&unit=([a-z-]+)").matcher(query);
            if (!selection.matches()) return "无效工厂参数。";
            Building target = Vars.world.build(Integer.parseInt(selection.group(1)), Integer.parseInt(selection.group(2)));
            if (!(target instanceof UnitFactoryBuild factory) || target.team != team) {
                return "只能配置己方单位工厂。";
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
            if (target == null || target.team == team || target.team == Team.derelict) {
                return "指定坐标没有可进攻的敌方建筑。";
            }
            return companion.attack(core, target);
        }
        if (route.equals("/guard")) {
            var guard = java.util.regex.Pattern.compile("x=([0-9]{1,5})&y=([0-9]{1,5})&seconds=([0-9]{1,3})").matcher(query);
            if (!guard.matches()) return "无效守卫参数。";
            Tile target = Vars.world.tile(Integer.parseInt(guard.group(1)), Integer.parseInt(guard.group(2)));
            int seconds = Integer.parseInt(guard.group(3));
            if (target == null || seconds < 5 || seconds > 600) return "守卫需要有效坐标和 5～600 秒的时间。";
            return companion.guard(core, target, seconds);
        }
        if (route.equals("/mine")) {
            var mining = java.util.regex.Pattern.compile("item=([a-z0-9-]{1,80})&amount=([0-9]{1,3})").matcher(query);
            if (!mining.matches() || Integer.parseInt(mining.group(2)) == 0) return "采运任务需要有效的物资ID和 1～999 的数量。";
            Item item = Vars.content.item(mining.group(1));
            if (item == null) return "找不到物资：" + mining.group(1) + "。";
            return companion.mine(core, item, Integer.parseInt(mining.group(2)));
        }
        if (route.equals("/supply")) {
            Planet planet = Vars.state.getPlanet();
            if (planet != null && planet.name.equals("erekir")) {
                return "传送带产线工具仅适用于苏普鲁。埃里克尔请用 MindustryBuild 建造导管、钻头、工厂和供电设施，并用 MindustryFactoryFeed 供料。";
            }
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
        if (route.equals("/build-line")) {
            var parameters = java.util.regex.Pattern.compile(
                "x1=(\\d+)&y1=(\\d+)&x2=(\\d+)&y2=(\\d+)&rotation=([0-3])&block=([a-z-]+)").matcher(query);
            if (!parameters.matches() || parameters.group(1).length() > 5 || parameters.group(2).length() > 5
                || parameters.group(3).length() > 5 || parameters.group(4).length() > 5) {
                return "无效连续建造参数。";
            }
            String name = parameters.group(6);
            Block block = Vars.content.block(name);
            if (block == null || block == Blocks.air || block.size != 1) return "连续建造只支持大小为 1 格的建筑：" + name;
            if (Vars.state.isCampaign() && !block.unlockedHost()) return "建筑尚未解锁：" + name + "。先查询科技树并研究。";
            if (block.isHidden() || Vars.state.rules.isBanned(block)) return "当前规则不允许建造：" + name;
            return buildLine(core, block,
                Integer.parseInt(parameters.group(1)), Integer.parseInt(parameters.group(2)),
                Integer.parseInt(parameters.group(3)), Integer.parseInt(parameters.group(4)),
                Integer.parseInt(parameters.group(5)));
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

    private String compactStatus(Team team, Building core) {
        Planet planet = Vars.state.getPlanet();
        StringBuilder result = new StringBuilder("关卡：").append(Vars.state.map.plainName())
            .append(" / ").append(planet == null ? "未知星球" : planet.name)
            .append(" ").append(Vars.world.width()).append("×").append(Vars.world.height())
            .append("，波次 ").append(Vars.state.wave).append("，核心 ").append(core.tileX()).append(",").append(core.tileY())
            .append("\n资源：");
        team.items().each((item, amount) -> result.append(item.name).append("=").append(amount).append(" "));
        if (Vars.state.rules.waves) {
            result.append("\n敌军：").append(Vars.state.enemies)
                .append(Vars.spawner.isSpawning() ? "，正在刷怪"
                    : Vars.state.enemies > 0 ? "，交战中" : "，已清场");
            if (Vars.state.rules.waveTimer && !Vars.spawner.isSpawning()
                && Vars.state.enemies == 0 && !Vars.state.isPaused()) {
                result.append("，下一波 ").append(Math.max(0, Math.round(Vars.state.wavetime / arc.util.Time.toSeconds))).append(" 秒");
            }
        }
        appendObjectives(result, team, 6);
        result.append("\n核心：").append(Math.round(core.healthf() * 100)).append("%，队伍 ")
            .append(team.name).append(" (").append(team.id).append(")");
        int players = 0;
        for (Player player : Groups.player) {
            if (players == 4) break;
            result.append("\n玩家：").append(player.plainName()).append(" / ")
                .append(player.team().name).append(" / ").append(player.tileX()).append(",").append(player.tileY());
            players++;
        }
        result.append("\n陪玩：").append(companion.unitStatus());
        result.append("\n作战：");
        int combatUnits = 0;
        for (Unit unit : team.data().units) {
            if (!unit.isValid() || unit.isPlayer() || unit == companion.worker()
                || !unit.type.weapons.contains(weapon -> weapon.aiControllable && !weapon.noAttack
                    && weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0)) continue;
            result.append(unit.type.name).append("@").append(unit.tileX()).append(",").append(unit.tileY())
                .append("/").append(Math.round(unit.healthf() * 100)).append("% ");
            if (++combatUnits == 6) break;
        }
        if (combatUnits == 0) result.append("无");
        result.append("\n当前：").append(companion.status());
        result.append("\n矿点：");
        int ores = 0;
        for (var material : Vars.indexer.getAllPresentOres()) {
            if (ores++ == 4) break;
            Tile floorOre = Vars.indexer.findClosestOre(core.x, core.y, material);
            Tile wallOre = Vars.indexer.findClosestWallOre(core.x, core.y, material);
            Tile ore = floorOre;
            String kind = "地板";
            if (wallOre != null && (floorOre == null || wallOre.dst2(core.x, core.y) < floorOre.dst2(core.x, core.y))) {
                ore = wallOre;
                kind = "墙体";
            }
            if (ore != null) {
                result.append(material.name).append("@").append(ore.x).append(",").append(ore.y)
                    .append("(").append(kind).append(") ");
            }
        }
        var threat = Units.closestEnemy(team, core.x, core.y, Vars.tilesize * 40,
            enemy -> !Vars.state.rules.fog || Vars.fogControl.isVisible(team, enemy.x, enemy.y));
        if (threat != null) {
            result.append("\n近敌：").append(threat.type.name).append("@")
                .append(threat.tileX()).append(",").append(threat.tileY());
        }
        int turrets = 0;
        for (Building building : team.data().buildings) {
            if (!(building instanceof ItemTurretBuild turret)) continue;
            if (turrets == 4) break;
            result.append("\n炮塔：").append(turret.block.name).append("@")
                .append(turret.tileX()).append(",").append(turret.tileY()).append("/")
                .append(Math.round(turret.getAmmoFraction() * 100)).append("%[")
                .append(ammoTypes(turret)).append("]");
            turrets++;
        }
        int targets = 0;
        for (var opponent : Vars.state.teams.getActive()) {
            if (opponent.team == team || opponent.team == Team.derelict) continue;
            for (Building enemyCore : opponent.cores) {
                if (targets == 4) break;
                result.append("\n敌核：").append(opponent.team.name).append("@")
                    .append(enemyCore.tileX()).append(",").append(enemyCore.tileY()).append("/")
                    .append(Math.round(enemyCore.healthf() * 100)).append("%");
                targets++;
            }
            if (targets == 4) break;
        }
        result.append("\n产能：");
        appendCompactIndustry(result, team, 8);
        if (Vars.state.rules.objectives != null) {
            for (MapObjective objective : Vars.state.rules.objectives.all) {
                if (!(objective instanceof BuildCountObjective build) || objective.isCompleted()) continue;
                result.append("\n待建：").append(build.block.name).append("（").append(requirements(build.block)).append("）");
                appendBuildPlacementCandidates(result, team, core, build.block, 2);
                break;
            }
        }
        return result.append("\n详情：MindustryStatus；全图：MindustryFind；局部：MindustrySurvey。").toString();
    }

    private void appendCompactIndustry(StringBuilder result, Team team, int limit) {
        int count = 0;
        for (Building building : team.data().buildings) {
            boolean relevant = building.block instanceof Drill
                || building.block instanceof GenericCrafter
                || building.block instanceof UnitFactory
                || building instanceof ItemTurretBuild
                || building instanceof mindustry.world.blocks.power.PowerGenerator.GeneratorBuild;
            if (!relevant) continue;
            result.append("\n- ").append(building.block.name).append("@")
                .append(building.tileX()).append(",").append(building.tileY()).append(" ")
                .append(building.status());
            if (building instanceof mindustry.world.blocks.power.PowerGenerator.GeneratorBuild generator) {
                result.append(" 发电=").append(Math.round(generator.getPowerProduction() * 60)).append("/秒");
            } else if (building.power != null && building.block.consPower != null) {
                result.append(" 需电=").append(Math.round(building.block.consPower.requestedPower(building) * 60)).append("/秒");
                if (building.power.status < 0.99f) {
                    result.append(" 满足=").append(Math.round(building.power.status * 100)).append("%");
                }
            } else if (building.power != null && building.power.status < 0.99f) {
                result.append(" 电=").append(Math.round(building.power.status * 100)).append("%");
            }
            if (building instanceof UnitFactoryBuild factory) {
                result.append(" 兵种=").append(factory.unit() == null ? "无" : factory.unit().name);
            }
            if (building instanceof ItemTurretBuild turret) {
                result.append(" 弹=").append(Math.round(turret.getAmmoFraction() * 100)).append("%");
            } else if (building.items != null) {
                building.items.each((item, amount) -> {
                    if (amount > 0) result.append(" ").append(item.name).append("=").append(amount);
                });
            }
            if (++count == limit) break;
        }
        if (count == 0) result.append("无");
    }

    private String researchStatus(Team team) {
        Sector currentSector = Vars.state.isCampaign() ? Vars.state.rules.sector : null;
        TechNode techRoot = currentTechRoot(currentSector);
        Planet planet = Vars.state.getPlanet();
        StringBuilder result = new StringBuilder("当前可研究项目（需求为剩余量）：星球 ")
            .append(planet == null ? "未知" : planet.localizedName + " (" + planet.name + ")");
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
                String display = objective.display();
                blockers.append(display == null || display.contains("???") ? "科技前置目标" : display);
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

        Map<Item, Character> itemSymbols = new LinkedHashMap<>();
        Map<String, Character> buildingSymbols = new LinkedHashMap<>();
        StringBuilder details = new StringBuilder();
        String buildingCodes = "1234567890!$%&()/;=[]{}|";
        StringBuilder result = new StringBuilder("勘查 中心=").append(x).append(',').append(y)
            .append(" 半径=").append(radius).append(" 上行y=").append(y + radius)
            .append(" 左x=").append(x - radius).append("；1字符=1格，x右 y下");
        for (int row = y + radius; row >= y - radius; row--) {
            result.append('\n');
            for (int column = x - radius; column <= x + radius; column++) {
                Tile tile = Vars.world.tile(column, row);
                if (tile == null) {
                    result.append('?');
                } else if (tile.build != null) {
                    if (tile.x != tile.build.tileX() || tile.y != tile.build.tileY()) {
                        result.append('+');
                        continue;
                    }
                    Building building = tile.build;
                    if (building.team != team) {
                        result.append('X');
                        appendSurveyBuildingDetail(details, building, 'X', team, itemSymbols);
                        continue;
                    }
                    if (building instanceof mindustry.world.blocks.storage.CoreBlock.CoreBuild) {
                        result.append('@');
                        appendSurveyBuildingDetail(details, building, '@', team, itemSymbols);
                        continue;
                    }
                    if (building.block instanceof Conveyor || building.block instanceof Duct) {
                        char arrow = surveyTransportArrow(building.rotation);
                        result.append(arrow);
                        continue;
                    }
                    Character symbol = buildingSymbols.get(building.block.name);
                    if (symbol == null) {
                        int index = buildingSymbols.size();
                        symbol = index < buildingCodes.length() ? buildingCodes.charAt(index) : '*';
                        buildingSymbols.put(building.block.name, symbol);
                    }
                    result.append(symbol);
                    appendSurveyBuildingDetail(details, building, symbol, team, itemSymbols);
                } else {
                    Item wallDrop = tile.wallDrop();
                    Item drop = tile.drop();
                    Block block = tile.block();
                    Floor floor = tile.floor();
                    if (wallDrop != null) {
                        result.append(Character.toUpperCase(itemSymbol(wallDrop, itemSymbols)));
                    } else if (drop != null) {
                        result.append(itemSymbol(drop, itemSymbols));
                    } else if (floor instanceof SteamVent) {
                        result.append('V');
                    } else if (block.isAir() == false) {
                        result.append('#');
                    } else if (floor.liquidDrop != null) {
                        result.append('~');
                    } else if (floor.hasSurface() == false) {
                        result.append('_');
                    } else {
                        result.append('.');
                    }
                }
            }
        }
        result.append("\n图例 .=空地 #=实体 ~=液体 V=喷口 +=占地 _=无地表 ?=界外 @=核心 X=敌；小写=地表矿 大写=墙矿 箭头=物流");
        if (itemSymbols.isEmpty() == false) {
            result.append("\n矿 ");
            for (Map.Entry<Item, Character> entry : itemSymbols.entrySet()) {
                char symbol = entry.getValue();
                result.append(symbol).append('=').append(entry.getKey().name).append(' ');
            }
            result.setLength(result.length() - 1);
        }
        if (buildingSymbols.isEmpty() == false) {
            result.append("\n建筑 ");
            for (Map.Entry<String, Character> entry : buildingSymbols.entrySet())
                result.append(entry.getValue()).append('=').append(entry.getKey()).append(' ');
            result.setLength(result.length() - 1);
        }
        if (details.length() > 0) result.append(details);
        return result.toString();
    }

    private String connectBeamNode(BeamNodeBuild build, BeamNode node, Building target) {
        build.updateDirections();
        if (java.util.Arrays.asList(build.links).contains(target))
            return "激光节点已连接目标。";

        int offsetX = target.tileX() - build.tileX();
        int offsetY = target.tileY() - build.tileY();
        if (offsetX != 0 && offsetY != 0) {
            return "目标未与激光节点同轴：偏移=" + offsetX + "," + offsetY
                + "。激光节点只沿上下左右连接；当前=" + beamLinkCoordinates(build) + "。";
        }
        if (target.block.hasPower == false || target.block.connectedPower == false) {
            return target.block.name + "@" + target.tileX() + "," + target.tileY()
                + " 不是可接电建筑；当前=" + beamLinkCoordinates(build) + "。";
        }
        if (Math.max(Math.abs(offsetX), Math.abs(offsetY)) > node.range) {
            return "目标距离 " + Math.max(Math.abs(offsetX), Math.abs(offsetY))
                + " 格，超过激光范围 " + node.range + "；当前=" + beamLinkCoordinates(build) + "。";
        }
        if (target.block instanceof PowerNode) {
            return "beam-node 不会连接普通电力节点；请直接连用电、发电或储电建筑。当前="
                + beamLinkCoordinates(build) + "。";
        }

        int stepX = Integer.compare(offsetX, 0);
        int stepY = Integer.compare(offsetY, 0);
        int distance = Math.max(Math.abs(offsetX), Math.abs(offsetY));
        for (int index = 1; index <= distance; index++) {
            Building crossed = Vars.world.build(build.tileX() + stepX * index, build.tileY() + stepY * index);
            if (crossed == null || crossed == build) continue;
            if (crossed.isInsulated()) {
                return "射线在 " + crossed.tileX() + "," + crossed.tileY() + " 被 "
                    + crossed.block.name + " 阻断；当前=" + beamLinkCoordinates(build) + "。";
            }
            if (crossed != target && crossed.team == build.team && crossed.block.hasPower
                && crossed.block.connectedPower && (crossed.block instanceof PowerNode) == false) {
                return "同轴更近处已有 " + crossed.block.name + "@" + crossed.tileX() + ","
                    + crossed.tileY() + "；beam-node 只连最近电力建筑，当前="
                    + beamLinkCoordinates(build) + "。";
            }
        }
        return "目标同轴且在范围内，但游戏没有建立连接；当前=" + beamLinkCoordinates(build)
            + "。请检查目标是否属于己方且可接电。";
    }

    private void appendSurveyBuildingDetail(StringBuilder details, Building building, char symbol, Team viewer,
                                            Map<Item, Character> itemSymbols) {
        boolean beam = building instanceof BeamNodeBuild;
        boolean carriesItems = building.items != null && building.items.total() > 0
            && building.items.total() < 1000000
            && (building.block instanceof GenericCrafter || building instanceof UnitFactoryBuild
                || building instanceof mindustry.world.blocks.storage.CoreBlock.CoreBuild);
        boolean hostile = building.team != viewer;
        if (hostile == false && beam == false && carriesItems == false) return;

        details.append('\n').append(symbol).append(' ').append(building.tileX()).append(',')
            .append(building.tileY());
        if (hostile) details.append(' ').append(building.block.name);
        if (building.block.rotate) details.append(" r").append(building.rotation);
        if (beam) {
            BeamNodeBuild beamBuild = (BeamNodeBuild)building;
            details.append(" 范围").append(((BeamNode)building.block).range)
                .append(" 连接=").append(beamLinkCoordinates(beamBuild));
        }
        if (carriesItems) {
            details.append(" 库存=");
            int[] count = {0};
            building.items.each((item, amount) -> {
                if (amount <= 0) return;
                if (count[0]++ > 0) details.append(',');
                details.append(itemSymbol(item, itemSymbols)).append(amount);
            });
        }
        if (building instanceof UnitFactoryBuild factory && factory.unit() != null)
            details.append(" 兵种=").append(factory.unit().name);
    }

    private char itemSymbol(Item item, Map<Item, Character> symbols) {
        Character existing = symbols.get(item);
        if (existing != null) return existing;
        String name = item.name.toLowerCase(Locale.ROOT);
        for (int index = 0; index < name.length(); index++) {
            char symbol = name.charAt(index);
            if (symbol < 'a' || symbol > 'z' || symbol == 'v' || symbol == 'x'
                || symbols.containsValue(symbol)) continue;
            symbols.put(item, symbol);
            return symbol;
        }
        for (char symbol = 'a'; symbol <= 'z'; symbol++) {
            if (symbol == 'v' || symbol == 'x' || symbols.containsValue(symbol)) continue;
            symbols.put(item, symbol);
            return symbol;
        }
        return 'o';
    }

    private char surveyTransportArrow(int rotation) {
        var direction = arc.math.geom.Geometry.d4(rotation);
        if (direction.x > 0) return '>';
        if (direction.x < 0) return '<';
        return direction.y > 0 ? '^' : 'v';
    }

    private String beamLinkCoordinates(BeamNodeBuild beam) {
        StringBuilder result = new StringBuilder();
        for (Building linked : beam.links) {
            if (linked == null) continue;
            if (result.length() > 0) result.append(',');
            result.append(linked.tileX()).append(',').append(linked.tileY());
        }
        return result.length() == 0 ? "无" : result.toString();
    }

    private String findObjects(Team team, Building core, String query) {
        if (query == null || query.length() > 160) return "缺少搜索参数。";
        var parameters = java.util.regex.Pattern.compile("kind=(ore|block|unit)&id=([a-z0-9-]{1,80})").matcher(query);
        if (!parameters.matches()) return "无效的搜索参数。";

        String kind = parameters.group(1);
        String id = parameters.group(2);
        Item oreItem = kind.equals("ore") ? Vars.content.item(id) : null;
        Block targetBlock = kind.equals("block") ? Vars.content.block(id) : null;
        mindustry.type.UnitType targetUnit = kind.equals("unit") ? Vars.content.unit(id) : null;

        PriorityQueue<FoundObject> nearest = new PriorityQueue<>(Comparator.comparingDouble(FoundObject::distance).reversed());
        int[] matches = {0};
        if (kind.equals("ore")) {
            for (int y = 0; y < Vars.world.height(); y++) {
                for (int x = 0; x < Vars.world.width(); x++) {
                    Tile tile = Vars.world.tile(x, y);
                    if (tile == null) continue;
                    Item wallDrop = tile.wallDrop();
                    Item drop = tile.drop();
                    if (wallDrop != null && matchesKeyword(wallDrop.name, oreItem, id)) {
                        keepNearest(nearest, matches, new FoundObject(tile.x, tile.y,
                            wallDrop.name + " (wall)", tile.dst2(core.x, core.y)));
                    }
                    if (drop != null && drop != wallDrop && matchesKeyword(drop.name, oreItem, id)) {
                        keepNearest(nearest, matches, new FoundObject(tile.x, tile.y,
                            drop.name + " (floor)", tile.dst2(core.x, core.y)));
                    }
                }
            }
        } else if (kind.equals("block")) {
            for (int y = 0; y < Vars.world.height(); y++) {
                for (int x = 0; x < Vars.world.width(); x++) {
                    Tile tile = Vars.world.tile(x, y);
                    if (tile == null) continue;
                    Block block = tile.block();
                    Floor floor = tile.floor();
                    if (block != null && block.isStatic()) {
                        if (targetBlock == block) {
                            keepNearest(nearest, matches, new FoundObject(x, y,
                                block.isStatic() ? "block:" + block.name : "solid:" + block.name,
                                tile.dst2(core.x, core.y)));
                        }
                        continue;
                    }
                    boolean floorMatch = matchesKeyword(floor.name, targetBlock, id)
                        && (!(floor instanceof SteamVent vent) || vent.isCenterVent(tile));
                    boolean blockMatch = block != null && block != Blocks.air
                        && matchesKeyword(block.name, targetBlock, id);
                    if (blockMatch == false && floorMatch == false) continue;
                    Building building = tile.build;
                    if (building != null) {
                        if (building.tileX() != x || building.tileY() != y) continue;
                    }
                    if (building != null) {
                        keepNearest(nearest, matches, new FoundObject(x, y,
                            (floorMatch ? "floor:" + floor.name + " " : "")
                                + (building.team == team ? "own:" : "team" + building.team.id + ":") + block.name
                                + " 生命=" + Math.round(building.healthf() * 100) + "%",
                            building.dst2(core.x, core.y)));
                    } else if (blockMatch) {
                        keepNearest(nearest, matches, new FoundObject(x, y,
                            block.isStatic() ? "block:" + block.name : "solid:" + block.name,
                            tile.dst2(core.x, core.y)));
                    } else {
                        keepNearest(nearest, matches, new FoundObject(x, y,
                            "floor:" + floor.name, tile.dst2(core.x, core.y)));
                    }
                }
            }
        } else {
            for (Unit unit : Groups.unit) {
                if (!unit.isValid() || !matchesKeyword(unit.type.name, targetUnit, id)
                    ) continue;
                keepNearest(nearest, matches, new FoundObject(unit.tileX(), unit.tileY(),
                    (unit.team == team ? "own:" : "team" + unit.team.id + ":") + unit.type.name
                        + " 生命=" + Math.round(unit.healthf() * 100) + "%",
                    unit.dst2(core.x, core.y)));
            }
        }

        if (matches[0] == 0) {
            if (kind.equals("ore")) return "全图没有匹配 " + id + " 的矿点。可用物资：" + itemNames(24);
            if (kind.equals("unit")) return "全图没有匹配 " + id + " 的单位。名称相近：" + unitNames(id, 8);
            return "全图没有匹配 " + id + " 的建筑。名称相近：" + blockNames(id, 8);
        }
        List<FoundObject> results = new ArrayList<>(nearest);
        results.sort(Comparator.comparingDouble(FoundObject::distance));
        StringBuilder result = new StringBuilder("全图目标共 ").append(matches[0])
            .append(" 个，按离核心由近到远最多显示 50 个：");
        for (FoundObject found : results) {
            result.append("\n- ").append(found.x).append(",").append(found.y).append(" ").append(found.description);
        }
        return result.toString();
    }

    private boolean matchesKeyword(String name, UnlockableContent exact, String keyword) {
        return keyword.equals("all") || (exact != null ? name.equals(exact.name) : name.contains(keyword));
    }

    private String itemNames(int limit) {
        StringBuilder result = new StringBuilder();
        int count = 0;
        for (Item item : Vars.content.items()) {
            if (count++ > 0) result.append(" ");
            result.append(item.name);
            if (count == limit) break;
        }
        return result.toString();
    }

    private String blockNames(String keyword, int limit) {
        StringBuilder result = new StringBuilder();
        int count = 0;
        for (Block block : Vars.content.blocks()) {
            if (block.name.contains(keyword) == false) continue;
            if (count++ > 0) result.append(" ");
            result.append(block.name);
            if (count == limit) break;
        }
        return count == 0 ? "无" : result.toString();
    }

    private String unitNames(String keyword, int limit) {
        StringBuilder result = new StringBuilder();
        int count = 0;
        for (mindustry.type.UnitType unit : Vars.content.units()) {
            if (unit.name.contains(keyword) == false) continue;
            if (count++ > 0) result.append(" ");
            result.append(unit.name);
            if (count == limit) break;
        }
        return count == 0 ? "无" : result.toString();
    }

    private static void keepNearest(PriorityQueue<FoundObject> nearest, int[] matches, FoundObject found) {
        matches[0]++;
        nearest.offer(found);
        if (nearest.size() > 50) nearest.poll();
    }

    private String describeObjective(MapObjective objective, Team team) {
        if (objective instanceof ItemObjective item) {
            return "获取 " + item.item.name + "：" + item.amount;
        }
        if (objective instanceof BuildCountObjective build) {
            int built = Vars.state.stats.placedBlockCount.get(build.block, 0);
            return "建造 " + build.block.name + "：" + built + "/" + build.count + "，建材=" + requirements(build.block);
        }
        if (objective instanceof CoreItemObjective item) {
            return "核心累计送达 " + item.item.name + "："
                + Vars.state.stats.coreItemCount.get(item.item, 0) + "/" + item.amount;
        }
        if (objective instanceof ResearchObjective research) {
            return "研究 " + research.content.name + "（" + research.content.localizedName + "），当前"
                + (research.content.unlockedHost() ? "已解锁" : "未解锁");
        }
        if (objective instanceof UnitCountObjective unit) {
            int count = 0;
            for (Unit current : team.data().units) {
                if (current.isValid() && current.type == unit.unit) count++;
            }
            return "拥有 " + unit.unit.name + "：" + count + "/" + unit.count;
        }
        if (objective instanceof CommandModeObjective) {
            return "进入指挥模式并接管己方单位";
        }
        if (objective instanceof DestroyUnitsObjective units) {
            return "摧毁敌方单位：" + units.count + " 个";
        }
        if (objective instanceof DestroyBlockObjective block) {
            return "摧毁 " + block.team.name + " 的 " + block.block.name + " @" + block.pos.x + "," + block.pos.y;
        }
        if (objective instanceof DestroyBlocksObjective blocks) {
            return "摧毁 " + blocks.team.name + " 的 " + blocks.block.name + "，" + blocks.positions.length + " 个位置";
        }
        if (objective instanceof TimerObjective timer) {
            return objectiveText(timer.text) + "，计时 " + Math.round(timer.duration / 60f) + " 秒";
        }
        if (objective instanceof FlagObjective flag) {
            return "完成旗标 " + flag.flag + "：" + objectiveText(flag.text);
        }
        String text = objective.text();
        return text == null || text.contains("???") ? objective.typeName() : text;
    }

    private void appendObjectives(StringBuilder result, Team team, int limit) {
        if (Vars.state.rules.objectives == null || Vars.state.rules.objectives.all.any() == false) return;

        List<MapObjective> available = new ArrayList<>();
        for (MapObjective objective : Vars.state.rules.objectives.all) {
            if (objective.qualified()) available.add(objective);
        }

        List<MapObjective> remaining = new ArrayList<>();
        int total = 0;
        int completed = 0;
        for (MapObjective objective : Vars.state.rules.objectives.all) {
            if (objective.hidden) continue;
            total++;
            if (objective.isCompleted()) {
                completed++;
                continue;
            }
            remaining.add(objective);
        }

        result.append("\n任务：").append(completed).append("/").append(total);
        if (completed == total) {
            result.append("，已全部完成");
            return;
        }

        MapObjective next = nextObjective(available, team);
        if (next == null) {
            result.append("，没有可执行目标");
            return;
        }

        result.append("\n- 下一步：").append(describeObjective(next, team));

        int shown = 1;
        for (MapObjective objective : available) {
            if (objective == next) continue;
            if (shown++ >= limit) break;
            result.append("\n- 后续：").append(describeObjective(objective, team));
        }

        if (next instanceof DestroyBlockObjective destroy) {
            if (hasCombatUnits(team) == false) {
                for (MapObjective objective : available) {
                    if (!(objective instanceof BuildCountObjective build) || objective.isCompleted()) continue;
                    if (build.block instanceof mindustry.world.blocks.units.UnitFactory == false) continue;
                    next = build;
                    break;
                }
                if (next instanceof BuildCountObjective build) {
                    result.setLength(result.lastIndexOf("\n- 下一步："));
                    result.append("\n- 下一步：").append(describeObjective(build, team));
                    for (MapObjective objective : available) {
                        if (objective == next) continue;
                        result.append("\n- 后续：").append(describeObjective(objective, team));
                    }
                }
            }
            appendAttackTargetCandidates(result, team, destroy);
        }

        for (MapObjective objective : remaining) {
            if (available.contains(objective)) continue;
            if (objective.hidden) continue;
            result.append("\n- 锁定：").append(describeObjective(objective, team));
            if (objective instanceof BuildCountObjective build) {
                for (MapObjective parent : build.parents) {
                    if (!(parent instanceof DestroyBlockObjective destroy) || parent.isCompleted()) continue;
                    result.append("；先摧毁 ").append(destroy.team.name).append(" 的 ")
                        .append(destroy.block.name).append(" @").append(destroy.pos.x).append(",").append(destroy.pos.y);
                    break;
                }
            }
            break;
        }

    }

    private MapObjective nextObjective(List<MapObjective> objectives, Team team) {
        for (MapObjective objective : objectives) {
            if (objective instanceof DestroyBlockObjective destroy
                && destroy.team != team && destroy.isCompleted() == false) return destroy;
        }
        for (MapObjective objective : objectives) {
            if (objective instanceof BuildCountObjective
                || objective instanceof ItemObjective
                || objective instanceof CoreItemObjective
                || objective instanceof ResearchObjective
                || objective instanceof UnitCountObjective
                || objective instanceof CommandModeObjective) return objective;
        }
        for (MapObjective objective : objectives) {
            if (objective instanceof DestroyUnitsObjective || objective instanceof DestroyBlocksObjective
                || objective instanceof TimerObjective) return objective;
        }
        if (objectives.isEmpty()) return null;
        for (MapObjective objective : objectives) {
            if (objective instanceof DestroyBlockObjective) continue;
            return objective;
        }
        return null;
    }

    private boolean hasCombatUnits(Team team) {
        for (Unit unit : team.data().units) {
            if (!unit.isValid() || unit.isPlayer() || unit.healthf() < 0.25f
                || !(unit.controller() instanceof mindustry.ai.types.CommandAI)) continue;
            if (unit.type.weapons.contains(weapon -> weapon.aiControllable && !weapon.noAttack
                && weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0)) return true;
        }
        return false;
    }

    private void appendAttackTargetCandidates(StringBuilder result, Team team, DestroyBlockObjective destroy) {
        List<Building> factories = new ArrayList<>();
        List<Unit> combatUnits = new ArrayList<>();
        for (Building building : team.data().buildings) {
            if (!(building instanceof UnitFactoryBuild) || building.isValid() == false) continue;
            factories.add(building);
        }
        for (Unit unit : team.data().units) {
            if (unit.isValid() && unit.isPlayer() == false && unit.healthf() >= 0.25f
                && unit.controller() instanceof mindustry.ai.types.CommandAI
                && unit.type.weapons.contains(weapon -> weapon.aiControllable && !weapon.noAttack
                    && weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0)) {
                combatUnits.add(unit);
            }
        }
        if (combatUnits.isEmpty()) {
            result.append("；先生产可攻城单位");
        } else {
            result.append("；可攻城=").append(combatUnits.size()).append(" 台");
        }
        if (factories.isEmpty()) {
            result.append("；没有出兵工厂");
            return;
        }
        factories.sort(Comparator.comparingDouble(building -> building.dst2(destroy.pos.x, destroy.pos.y)));
        Building factory = factories.get(0);
        result.append("；出兵点 ").append(factory.block.name).append("@")
            .append(factory.tileX()).append(",").append(factory.tileY());
    }

    private String objectiveText(String text) {
        if (text == null || text.isBlank()) return "未提供说明";
        if (text.equals("@objective.enemiesapproaching")) return "等待敌军接近";
        if (text.equals("@onset.defenses")) return "守住始发地区防线";
        if (text.equals("@gz.supplyturret")) return "给防御炮塔供应弹药";
        return text.startsWith("@") ? text.substring(1) : text;
    }

    private String requirements(Block block) {
        if (block.requirements.length == 0) return "无";
        StringBuilder result = new StringBuilder();
        for (ItemStack requirement : block.requirements) {
            if (result.length() > 0) result.append(" ");
            result.append(requirement.item.name).append("=")
                .append(Math.round(requirement.amount * Vars.state.rules.buildCostMultiplier));
        }
        return result.toString();
    }

    private String itemRequirements(ItemStack[] stacks) {
        if (stacks.length == 0) return "无";
        StringBuilder result = new StringBuilder();
        for (ItemStack requirement : stacks) {
            if (result.length() > 0) result.append(" ");
            result.append(requirement.item.name).append("=").append(requirement.amount);
        }
        return result.toString();
    }

    private String consumedItems(Block block) {
        for (Consume consumer : block.nonOptionalConsumers) {
            if (consumer instanceof ConsumeItems items) return itemRequirements(items.items);
        }
        return null;
    }

    private void appendIndustrySummary(StringBuilder result, Team team, int limit) {
        int count = 0;
        List<Building> logistics = new ArrayList<>();
        List<Building> production = new ArrayList<>();
        List<Building> power = new ArrayList<>();
        for (Building building : team.data().buildings) {
            if (building.block instanceof Duct || building.block instanceof Conveyor) {
                logistics.add(building);
                continue;
            }
            boolean productive = building.block instanceof Drill
                || building.block instanceof GenericCrafter
                || building.block instanceof UnitFactory
                || building instanceof ItemTurretBuild;
            if (productive) {
                production.add(building);
            } else if (building.power != null) {
                power.add(building);
            }
        }
        production.sort(Comparator.comparingInt(Building::tileY).thenComparingInt(Building::tileX));
        power.sort(Comparator.comparingInt(Building::tileY).thenComparingInt(Building::tileX));
        for (Building building : production) {
            if (count++ == limit) break;
            appendIndustryBuilding(result, building);
        }
        for (Building building : power) {
            if (count++ == limit) break;
            appendIndustryBuilding(result, building);
        }
        if (count == 0 && logistics.isEmpty()) {
            result.append("\n- 无");
            return;
        }
        if (logistics.isEmpty()) return;
        logistics.sort(Comparator.comparingInt(Building::tileY).thenComparingInt(Building::tileX));
        result.append("\n物流建筑（按坐标排序，最多显示 ").append(limit).append(" 个）：");
        for (int index = 0; index < Math.min(logistics.size(), limit); index++) {
            appendIndustryBuilding(result, logistics.get(index));
        }
    }

    private void appendIndustryBuilding(StringBuilder result, Building building) {
        result.append("\n- ").append(building.block.name).append("@")
            .append(building.tileX()).append(",").append(building.tileY())
            .append(" ").append(building.status());
        if (building.block.rotate) result.append(" 方向=").append(building.rotation);
        if (building instanceof BeamNodeBuild beam) {
            result.append(" 连接=").append(beamLinkCoordinates(beam));
        } else if (building instanceof mindustry.world.blocks.power.PowerGenerator.GeneratorBuild generator) {
            result.append(" 发电=").append(Math.round(generator.getPowerProduction() * 60)).append("/秒");
        } else if (building.power != null) {
            result.append(" 电力满足=").append(Math.round(building.power.status * 100)).append("%");
        }
        if (building.block instanceof GenericCrafter) {
            String inputs = consumedItems(building.block);
            if (inputs != null) result.append(" 原料=").append(inputs);
        }
        if (building instanceof UnitFactoryBuild factory) {
            result.append(" 兵种=").append(factory.unit() == null ? "未选择" : factory.unit().name)
                .append(" 进度=").append(Math.round(factory.fraction() * 100)).append("%");
            UnitFactory unitFactory = (UnitFactory)factory.block;
            if (factory.currentPlan >= 0 && factory.currentPlan < unitFactory.plans.size) {
                result.append(" 所需=")
                    .append(itemRequirements(unitFactory.plans.get(factory.currentPlan).requirements));
            }
            Tile exit = factoryExit(factory.tileX(), factory.tileY(), factory.block, factory.rotation);
            if (exit != null) result.append(" 出口=").append(exit.x).append(",").append(exit.y);
        }
        if (building instanceof ItemTurretBuild turret) {
            result.append(" 弹药=").append(Math.round(turret.getAmmoFraction() * 100)).append("%")
                .append(" 可装=").append(ammoTypes(turret));
        }
        if (building.items != null) {
            building.items.each((item, amount) -> {
                if (amount > 0) result.append(" ").append(item.name).append("=").append(amount);
            });
        }
    }

    private String ammoTypes(ItemTurretBuild turret) {
        StringBuilder result = new StringBuilder();
        for (Item item : Vars.content.items()) {
            if (!((ItemTurret)turret.block).ammoTypes.containsKey(item)) continue;
            if (result.length() > 0) result.append("/");
            result.append(item.name);
        }
        return result.toString();
    }

    private boolean acceptsSupply(Building target, Item material) {
        if (target instanceof ItemTurretBuild turret) {
            return ((ItemTurret)turret.block).ammoTypes.get(material) != null;
        }
        return target.getMaximumAccepted(material) > 0;
    }

    private void appendAvailableBlocks(StringBuilder result, Team team, Planet planet, Building core) {
        List<Block> blocks = new ArrayList<>();
        List<Block> missionBlocks = new ArrayList<>();
        if (Vars.state.rules.objectives != null) {
            for (MapObjective objective : Vars.state.rules.objectives) {
                if (!(objective instanceof BuildCountObjective build) || objective.isCompleted()
                    || objective.qualified() == false) continue;
                if (!missionBlocks.contains(build.block)) missionBlocks.add(build.block);
            }
        }
        for (Block block : missionBlocks) {
            blocks.add(block);
            appendBlock(result, "[任务] ", block);
            appendBuildPlacementCandidates(result, team, core, block, 3);
        }
        for (Building building : team.data().buildings) {
            if (blocks.contains(building.block)) continue;
            blocks.add(building.block);
            appendBlock(result, "[已有] ", building.block);
            if (blocks.size() == 20) break;
        }
        if (blocks.size() >= 20 || planet == null) return;
        for (TechNode node : TechTree.all) {
            if (node.rootNode != planet.techTree || !(node.content instanceof Block block)
                || block.isHidden() || block.requirements.length == 0 || !block.isOnPlanet(planet)
                || (!block.unlockedHost() && !block.alwaysUnlocked) || blocks.contains(block)) continue;
            blocks.add(block);
            appendBlock(result, "[已解锁] ", block);
            if (blocks.size() == 20) return;
        }
    }

    private void appendBlock(StringBuilder result, String prefix, Block block) {
        result.append("\n- ").append(prefix).append(block.name)
            .append("（建材：").append(requirements(block)).append("）");
    }

    private void appendBuildPlacementCandidates(StringBuilder result, Team team, Building core, Block block, int limit) {
        appendPlacementCandidates(result, team, core, block, limit);
    }

    private void appendPlacementCandidates(StringBuilder result, Team team, Building core, Block block, int limit) {
        result.append("；候选=");
        if (Vars.state.rules.objectives != null) {
            for (MapObjective objective : Vars.state.rules.objectives.all) {
                if (!(objective instanceof DestroyBlockObjective destroy) || !destroy.isCompleted()
                    || destroy.block != block) continue;
                for (int dy = -1; dy <= 1; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        int x = destroy.pos.x + dx;
                        int y = destroy.pos.y + dy;
                        if (!Build.validPlace(block, team, x, y, 0)) continue;
                        result.append(x).append(",").append(y);
                        return;
                    }
                }
            }
        }
        int found = 0;
        int maxDistance = Math.max(12, block.size * 3);
        for (int distance = 2; distance <= maxDistance && found < limit; distance++) {
            for (int dx = -distance; dx <= distance && found < limit; dx++) {
                int dy = distance - Math.abs(dx);
                for (int sign = -1; sign <= 1 && found < limit; sign += 2) {
                    if (dy == 0 && sign == 1) continue;
                    int x = core.tileX() + dx;
                    int y = core.tileY() + dy * sign;
                    if (!Build.validPlace(block, team, x, y, 0)) continue;
                    if (found++ > 0) result.append(" ");
                    result.append(x).append(",").append(y);
                }
            }
        }
        if (found == 0) result.append("未找到可用位置，请勘查后换个区域");
    }

    private void appendClosestBlocks(StringBuilder result, Team team, Building core, String keyword, int limit) {
        PriorityQueue<FoundObject> nearest = new PriorityQueue<>(Comparator.comparingDouble(FoundObject::distance).reversed());
        int[] matches = {0};
        for (int y = 0; y < Vars.world.height(); y++) {
            for (int x = 0; x < Vars.world.width(); x++) {
                Tile tile = Vars.world.tile(x, y);
                if (tile == null) continue;
                Block block = tile.block();
                Floor floor = tile.floor();
                boolean floorMatch = floor.name.contains(keyword)
                    && (!(floor instanceof SteamVent vent) || vent.isCenterVent(tile));
                boolean blockMatch = block != null && block != Blocks.air && block.name.contains(keyword);
                if (floorMatch == false && blockMatch == false) continue;
                String description = floorMatch ? "floor:" + floor.name : "block:" + block.name;
                if (tile.build != null) description += "(" + tile.build.block.name + ")";
                keepNearest(nearest, matches, new FoundObject(x, y, description, tile.dst2(core.x, core.y)));
            }
        }
        if (matches[0] == 0) {
            result.append("未发现");
            return;
        }
        List<FoundObject> results = new ArrayList<>(nearest);
        results.sort(Comparator.comparingDouble(FoundObject::distance));
        result.append("共 ").append(matches[0]).append(" 个，最近 ").append(Math.min(limit, results.size())).append(" 个：");
        for (int index = 0; index < Math.min(limit, results.size()); index++) {
            FoundObject found = results.get(index);
            result.append(found.description).append("@").append(found.x).append(",").append(found.y).append(" ");
        }
    }

    private Tile factoryExit(int x, int y, Block block, int rotation) {
        var direction = arc.math.geom.Geometry.d4(rotation);
        int distance = block.size / 2 + 1;
        return Vars.world.tile(x + direction.x * distance, y + direction.y * distance);
    }

    private String build(Building core, Block block, int x, int y, int rotation) {
        String error = validateBuildLocation(core, block, x, y, rotation);
        if (error != null) return error;
        if (!hasBuildResources(core.team, block, 1)) return missingBuildResources(core.team, block, 1);
        return companion.build(core, new BuildPlan(x, y, rotation, block));
    }

    private String buildLine(Building core, Block block, int x1, int y1, int x2, int y2, int rotation) {
        int count = Math.abs(x2 - x1) + Math.abs(y2 - y1) + 1;
        if (count > 96) return "连续建造最多 96 格，请分段提交。";
        List<int[]> route = new ArrayList<>();
        int stepX = Integer.compare(x2, x1);
        for (int x = x1; ; x += stepX) {
            route.add(new int[]{x, y1});
            if (x == x2) break;
        }
        int stepY = Integer.compare(y2, y1);
        if (stepY != 0) {
            for (int y = y1 + stepY; ; y += stepY) {
                route.add(new int[]{x2, y});
                if (y == y2) break;
            }
        }
        List<BuildPlan> plans = new ArrayList<>();
        for (int index = 0; index < route.size(); index++) {
            int[] position = route.get(index);
            int direction = route.size() == 1 ? rotation
                : pathDirection(route.get(Math.max(0, index - 1)), route.get(Math.min(route.size() - 1, index + 1)));
            String error = validateBuildLocation(core, block, position[0], position[1], direction);
            if (error != null) return error;
            plans.add(new BuildPlan(position[0], position[1], direction, block));
        }
        if (!hasBuildResources(core.team, block, plans.size())) {
            return missingBuildResources(core.team, block, plans.size());
        }
        return companion.buildRoute(core, block, plans);
    }

    private int pathDirection(int[] from, int[] to) {
        if (to[0] > from[0]) return 0;
        if (to[1] > from[1]) return 1;
        if (to[0] < from[0]) return 2;
        return 3;
    }

    private String validateBuildLocation(Building core, Block block, int x, int y, int rotation) {
        Team team = core.team;
        Tile tile = Vars.world.tile(x, y);
        if (tile == null) {
            return "建造位置在地图外：" + x + "," + y + "。";
        }
        if (tile.block() != Blocks.air || !Build.validPlace(block, team, x, y, rotation)) {
            return block.name + " 无法放在 " + x + "," + y
                + "：格子被占用、地形不适合或方向不合法。请先用 MindustrySurvey " + x + " " + y + " 3 勘查。";
        }
        if (block instanceof UnitFactory) {
            Tile exit = factoryExit(x, y, block, rotation);
            if (exit == null || exit.solid()) return block.name + " 需要朝向空旷的出兵口。";
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
        return null;
    }

    private boolean hasBuildResources(Team team, Block block, int count) {
        if (Vars.state.rules.isInfiniteResources(team)) return true;
        for (ItemStack requirement : block.requirements) {
            int required = Math.round(requirement.amount * Vars.state.rules.buildCostMultiplier * count);
            if (!team.items().has(requirement.item, required)) return false;
        }
        return true;
    }

    private String missingBuildResources(Team team, Block block, int count) {
        StringBuilder missing = new StringBuilder();
        for (ItemStack requirement : block.requirements) {
            int required = Math.round(requirement.amount * Vars.state.rules.buildCostMultiplier * count);
            int available = team.items().get(requirement.item);
            if (missing.length() > 0) missing.append(" ");
            missing.append(requirement.item.name).append(" 需要").append(required).append("/现有").append(available);
        }
        return "核心资源不足，无法建造 " + count + " 个 " + block.name + "：" + missing + "。可先采矿补齐。";
    }
}
