package alife.mindustry;

import arc.math.geom.Vec2;
import mindustry.Vars;
import mindustry.ai.types.CommandAI;
import mindustry.content.Items;
import mindustry.world.blocks.defense.turrets.ItemTurret.ItemTurretBuild;
import mindustry.entities.Units;
import mindustry.entities.units.BuildPlan;
import mindustry.gen.Building;
import mindustry.gen.Call;
import mindustry.gen.Unit;
import mindustry.type.Item;
import mindustry.world.Block;
import mindustry.world.Build;
import mindustry.world.Tile;
import mindustry.world.blocks.defense.turrets.ItemTurret;
import mindustry.world.blocks.defense.turrets.ItemTurret.ItemTurretBuild;
import mindustry.world.blocks.storage.CoreBlock;

import arc.util.Time;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;

final class CompanionTasks {
    private enum Action { MOVE, BUILD, MINE, VERIFY_SUPPLY, GUARD, RETREAT, RESUPPLY, TRANSFER, ATTACK, COLLECT, UNLOAD }

    private Unit worker;
    private Action action;
    private Tile destination;
    private BuildPlan plan;
    private final ArrayDeque<BuildPlan> construction = new ArrayDeque<>();
    private int completedBuildings;
    private Building supplyCore;
    private Item supplyItem;
    private int supplyBaseline;
    private Building home;
    private float guardTime;
    private Building turret;
    private Building transferSource;
    private Item cargo;
    private Building attackTarget;
    private final List<Unit> attackers = new ArrayList<>();
    private float attackStagingTime;
    private Action afterUnload;
    private Tile guardPost;
    private float reloadCheck;
    private int guardAmmoDelivered;
    private Item item;
    private Building depositCore;
    private Tile ore;
    private int remaining;
    private int delivered;
    private float miningRetry;
    private float blockedTime;
    private float timeLeft;
    private long sequence;
    private final Map<Item, List<BuildPlan>> supplyRoutes = new HashMap<>();
    private final Map<Long, String> completedTasks = new LinkedHashMap<>();
    private boolean miningReturn;
    private boolean buildingQueued;
    private String task = "暂无任务";
    private String phase = "";

    void reset() {
        completedTasks.clear();
        supplyRoutes.clear();
        worker = null;
        action = null;
        task = "暂无任务";
        phase = "";
        clearTargets();
    }

    long sequence() {
        return sequence;
    }

    String completedTask(long id) {
        return completedTasks.get(id);
    }

    boolean isRunning() {
        return action != null;
    }

    boolean canAdvanceWaveWhileGuarding() {
        return action == Action.GUARD && Vars.state.rules.waves && Vars.state.rules.waveSending
            && Vars.state.enemies == 0 && !Vars.spawner.isSpawning();
    }

    String status() {
        return task + (phase.isEmpty() ? "" : " " + phase);
    }

    String unitStatus() {
        if (!ready()) return "将在首条有效指令后从己方核心出发";
        String status = worker.type.name + "@" + worker.tileX() + "," + worker.tileY()
            + "，生命 " + Math.round(worker.health) + "/" + Math.round(worker.maxHealth);
        if (worker.stack.amount > 0) status += "，携带 " + worker.stack.item.name + "=" + worker.stack.amount;
        if (action == Action.ATTACK) status += "，进攻编队 " + attackers.size() + " 台";
        return status;
    }

    Unit worker() {
        return worker;
    }

    String move(Building core, Tile target) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        start(Action.MOVE, "移动到 " + target.x + "," + target.y, 45);
        navigate(target);
        return accepted();
    }

    String attack(Building core, Building target) {
        if (action != null) return busy();
        attackers.clear();
        for (Unit candidate : core.team.data().units) {
            if (candidate != worker && candidate.isValid() && !candidate.isPlayer()
                && candidate.healthf() >= 0.25f && candidate.controller() instanceof CommandAI
                && candidate.type.weapons.contains(weapon -> weapon.aiControllable && !weapon.noAttack
                    && weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0)) {
                attackers.add(candidate);
            }
        }
        if (attackers.isEmpty()) return "己方没有可用的攻城单位，请先生产可以伤害建筑的作战单位。";
        start(Action.ATTACK, "进攻 " + target.block.name + "@" + target.tileX() + "," + target.tileY(), 900);
        home = core;
        attackTarget = target;
        destination = attackStaging(target);
        attackStagingTime = 30f * 60f;
        for (Unit attacker : attackers) {
            ((CommandAI)attacker.controller()).commandPosition(
                new Vec2(destination.worldx(), destination.worldy()));
        }
        phase = "正在前往 " + destination.x + "," + destination.y + " 集结，编队 " + attackers.size() + " 台";
        return accepted();
    }

    String guard(Building core, Tile target, int seconds) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        start(Action.GUARD, "守卫 " + target.x + "," + target.y + "（" + seconds + " 秒）", seconds + 90);
        home = core;
        guardTime = seconds * 60f;
        guardPost = target;
        reloadCheck = 0;
        guardAmmoDelivered = 0;
        if (worker.stack.amount > 0) {
            afterUnload = Action.GUARD;
            action = Action.UNLOAD;
            depositCore = core;
            destination = core.tile;
            navigate(core.tile);
            phase = "先返回核心卸下余料";
        } else {
            navigate(target);
        }
        return accepted();
    }

    String build(Building core, BuildPlan target) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        return buildQueue(target.block.name + "@" + target.x + "," + target.y, List.of(target), 90);
    }

    String buildRoute(Building core, Block block, List<BuildPlan> targets) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        return buildQueue(block.name + " 路线（" + targets.size() + " 格）", targets, 90 + targets.size() * 3);
    }

    String supply(Building core, Item item, List<BuildPlan> route) {
        if (action != null) return busy();
        if (route.isEmpty()) return "找不到从矿点到核心的连续空地，或者该矿点尚不可见。";
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        supplyRoutes.putIfAbsent(item, List.copyOf(route));
        supplyCore = core;
        supplyItem = item;
        return buildQueue(item.name + " 产线（" + route.size() + " 格建筑）", route, 180);
    }

    List<BuildPlan> damagedSupply(Building core, Item item) {
        List<BuildPlan> route = supplyRoutes.get(item);
        if (route == null) return List.of();
        return route.stream().filter(plan -> {
            Tile tile = Vars.world.tile(plan.x, plan.y);
            return tile.block() != plan.block || tile.team() != core.team
                || (plan.block.rotate && tile.build.rotation != plan.rotation);
        }).map(plan -> new BuildPlan(plan.x, plan.y, plan.rotation, plan.block)).toList();
    }

    boolean knowsSupply(Item item) {
        return supplyRoutes.containsKey(item);
    }

    private String buildQueue(String description, List<BuildPlan> plans, int seconds) {
        start(Action.BUILD, description, seconds);
        construction.addAll(plans);
        completedBuildings = 0;
        nextPlacement();
        return accepted();
    }

    String mine(Building core, Item target, int amount) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        if (!worker.canMine() || !worker.canMine(target)) {
            return "陪玩单位 " + worker.type.name + " 无法直接开采 " + target.name
                + "。请建造当前星球可用的钻矿建筑或在 MindustryStatus 查看矿点。";
        }

        Tile source = findOre(target);
        if (source == null) {
            StringBuilder available = new StringBuilder();
            for (Item item : Vars.indexer.getAllPresentOres()) {
                if (available.length() > 0) available.append("、");
                available.append(item.name);
            }
            return "附近没有可开采的 " + target.name + "。地图当前存在的矿种："
                + (available.length() == 0 ? "无" : available.toString());
        }
        if (core.acceptStack(target, 1, worker) == 0) return "核心无法接收 " + target.name + "。";
        start(Action.MINE, "采运 " + amount + " " + target.name, Math.max(150, amount * 2));
        ore = source;
        item = target;
        remaining = amount;
        delivered = 0;
        depositCore = core;
        miningReturn = worker.stack.item == target && worker.stack.amount >= amount;
        if (needsUnload(target)) beginUnload(Action.MINE);
        else if (miningReturn) navigateCore();
        else navigateOre();
        return accepted();
    }

    String resupply(Building core, Building target, Item material, int amount) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        int carried = worker.stack.item == material ? worker.stack.amount : 0;
        if (!accepts(target, material)) return "目标建筑不接收该物资。";
        if (core.items.get(material) + carried <= 0) return "核心可用物资不足。";

        start(Action.RESUPPLY, "物资补给 " + target.tileX() + "," + target.tileY(), 240 + amount * 4);
        cargo = material;
        home = core;
        turret = target;
        remaining = Math.min(amount, core.items.get(material) + carried);
        delivered = 0;
        if (carried >= remaining) navigate(turret.tile);
        else navigate(home.tile);
        return accepted();
    }

    String transfer(Building core, Building source, Building target, Item material, int amount) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        if (worker.stack.amount > 0 && worker.stack.item != material) {
            return "陪玩单位正携带 " + worker.stack.item.name + "，无法同时转运 " + material.name + "。";
        }

        if (!accepts(target, material)) return "目标建筑不接收 " + material.name + "。";
        int carried = worker.stack.item == material ? worker.stack.amount : 0;
        int available = Math.min(amount, source.items.get(material) + carried);
        if (available <= 0) return "源建筑没有可转运的 " + material.name + "。";

        start(Action.TRANSFER, "转运 " + material.name + " "
            + source.tileX() + "," + source.tileY() + "→" + target.tileX() + "," + target.tileY(), 120);
        item = material;
        transferSource = source;
        turret = target;
        remaining = available;
        delivered = 0;
        navigate(source.tile);
        return accepted();
    }

    String collectSilicon(Building core, Building source, int amount) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";

        int available = Math.min(amount, Math.min(source.items.get(Items.silicon), (needsUnload(Items.silicon) ? worker.type.itemCapacity : worker.maxAccepted(Items.silicon))));
        if (available <= 0 || core.acceptStack(Items.silicon, 1, worker) <= 0) return "暂无可运回核心的硅。";
        start(Action.COLLECT, "回收冶炼炉产出的硅", 90);
        turret = source;
        depositCore = core;
        item = Items.silicon;
        remaining = available;
        if (needsUnload(item)) beginUnload(Action.COLLECT);
        else navigate(source.tile);
        return accepted();
    }

    private boolean needsUnload(Item next) {
        return worker.stack.amount > 0 && worker.stack.item != next;
    }

    private void beginUnload(Action next) {
        afterUnload = next;
        action = Action.UNLOAD;
        navigate(depositCore.tile);
        phase = "返回核心卸下余料";
    }

    private void updateUnload() {
        if (!depositCore.isValid() || depositCore.team != worker.team) {
            finish("己方核心已失效，无法卸下余料");
            return;
        }
        if (!worker.within(depositCore, worker.type.range)) return;
        if (!unloadCargo(depositCore)) {
            finish("核心没有足够空间卸下余料");
            return;
        }
        action = afterUnload;
        if (action == Action.MINE) navigateOre();
        else if (action == Action.GUARD) navigate(guardPost);
        else navigate(turret.tile);
    }

    private boolean unloadCargo(Building core) {
        Item previous = worker.stack.item;
        int amount = worker.stack.amount;
        if (core.acceptStack(previous, amount, worker) < amount) return false;
        transferToCore(core, previous, amount);
        return true;
    }

    private boolean accepts(Building target, Item material) {
        if (target instanceof ItemTurretBuild) {
            return ((ItemTurret)target.block).ammoTypes.get(material) != null;
        }
        return target.getMaximumAccepted(material) > 0;
    }

    private void updateCollection() {
        if (!turret.isValid() || turret.team != worker.team || !depositCore.isValid() || depositCore.team != worker.team) {
            finish("冶炼炉或己方核心已失效");
            return;
        }
        if (destination == turret.tile) {
            if (!worker.within(turret, worker.type.range)) return;
            int previous = worker.stack.amount;
            Call.takeItems(turret, item, remaining, worker);
            remaining = worker.stack.amount - previous;
            if (remaining == 0) {
                finish("冶炼炉已经没有可取的硅");
                return;
            }
            navigate(depositCore.tile);
            return;
        }
        if (!worker.within(depositCore, worker.type.range)) return;
        int amount = depositCore.acceptStack(item, remaining, worker);
        if (amount <= 0) {
            finish("核心无法接收硅，已取出的硅仍在单位背包");
            return;
        }
        transferToCore(depositCore, item, amount);
        finish("已运入核心 " + amount + " silicon");
    }

    String cancel() {
        if (action == null) return "当前没有运行中的陪玩任务。";
        finish("已取消");
        return status();
    }

    void update() {
        if (action == null) return;
        if (action != Action.VERIFY_SUPPLY && action != Action.ATTACK && !ready()) {
            finish("失败：单位已损毁");
            return;
        }
        if (Vars.state.isPaused()) return;
        timeLeft -= Time.delta;
        if (!Vars.state.isGame() || Vars.state.gameOver || timeLeft <= 0) {
            finish(action == Action.VERIFY_SUPPLY ? "产线已建造，暂未观察到核心库存增加" : "未完成：对局结束或执行超时");
            return;
        }
        switch (action) {
            case MOVE -> {
                if (worker.within(destination.worldx(), destination.worldy(), Vars.tilesize)) {
                    finish("已到达");
                    return;
                }
                ensureNavigation();
            }
            case BUILD -> {
                ensureNavigation();
                updateBuild();
            }
            case MINE -> {
                ensureNavigation();
                updateMining();
            }
            case VERIFY_SUPPLY -> verifySupply();
            case GUARD, RETREAT -> updateGuard();
            case RESUPPLY -> {
                ensureNavigation();
                updateResupply();
            }
            case TRANSFER -> {
                ensureNavigation();
                updateTransfer();
            }
            case ATTACK -> updateAttack();
            case COLLECT -> {
                ensureNavigation();
                updateCollection();
            }
            case UNLOAD -> {
                ensureNavigation();
                updateUnload();
            }
        }
    }

    void constructionFinished(Unit builder, Tile tile) {
        if (action == Action.BUILD && worker == builder && tile.x == plan.x && tile.y == plan.y && tile.block() == plan.block) {
            completedBuildings++;
            if (!construction.isEmpty()) {
                nextPlacement();
            } else if (supplyCore == null) {
                finish("已完工");
            } else {
                action = Action.VERIFY_SUPPLY;
                supplyBaseline = supplyCore.items.get(supplyItem);
                phase = "已建造 " + completedBuildings + " 处，等待核心入库";
                timeLeft = 45f * 60;
            }
        }
    }

    private void updateAttack() {
        if (!attackTarget.isValid()) {
            finish("敌方建筑已摧毁");
            return;
        }
        if (attackTarget.team == home.team) {
            finish("目标已不属于敌方");
            return;
        }

        for (int index = attackers.size() - 1; index >= 0; index--) {
            Unit attacker = attackers.get(index);
            if (!attacker.isValid() || attacker.isPlayer() || attacker.team != home.team
                || !(attacker.controller() instanceof CommandAI combat)) {
                attackers.remove(index);
                continue;
            }
            if (attacker.healthf() < 0.25f) {
                if (home.isValid()) combat.commandPosition(new Vec2(home.x, home.y));
                attackers.remove(index);
            }
        }
        if (attackers.isEmpty()) {
            finish("作战编队已损毁或重伤返航");
            return;
        }
        if (attackStagingTime > 0) {
            attackStagingTime -= Time.delta;
            if (attackStagingTime > 0) {
                boolean staged = true;
                for (Unit attacker : attackers) {
                    if (!attacker.within(destination.worldx(), destination.worldy(), Vars.tilesize * 3f)) {
                        staged = false;
                        break;
                    }
                }
                if (!staged) {
                    for (Unit attacker : attackers) {
                        CommandAI combat = (CommandAI)attacker.controller();
                        if (!combat.hasCommand()) combat.commandPosition(
                            new Vec2(destination.worldx(), destination.worldy()));
                    }
                    return;
                }
            }
            attackStagingTime = 0;
            for (Unit attacker : attackers) ((CommandAI)attacker.controller()).commandTarget(attackTarget);
            phase = "已在外侧集结，正在接敌，编队 " + attackers.size() + " 台";
            return;
        }
        for (Unit attacker : attackers) {
            CommandAI combat = (CommandAI)attacker.controller();
            if (!combat.hasCommand()) combat.commandTarget(attackTarget);
        }
        phase = "正在接敌，剩余 " + attackers.size() + " 台";
    }

    private Tile attackStaging(Building target) {
        int offset = target.block.size / 2 + 3;
        Tile[] candidates = {
            Vars.world.tile(target.tileX(), target.tileY() + offset),
            Vars.world.tile(target.tileX(), target.tileY() - offset),
            Vars.world.tile(target.tileX() - offset, target.tileY()),
            Vars.world.tile(target.tileX() + offset, target.tileY())
        };
        Tile best = null;
        int bestScore = Integer.MIN_VALUE;
        int fewestBlockers = Integer.MAX_VALUE;
        float greatestDistance = -1f;
        for (Tile candidate : candidates) {
            if (candidate == null || candidate.solid()) continue;
            int score = 0;
            for (int dy = -2; dy <= 2; dy++) {
                for (int dx = -2; dx <= 2; dx++) {
                    Tile tile = Vars.world.tile(candidate.x + dx, candidate.y + dy);
                    if (tile != null && !tile.solid()) score++;
                }
            }
            int blockers = 0;
            int directionX = Integer.signum(candidate.x - target.tileX());
            int directionY = Integer.signum(candidate.y - target.tileY());
            for (int step = 1; step <= offset; step++) {
                for (int lateral = -1; lateral <= 1; lateral++) {
                    int x = target.tileX() + directionX * step
                        + (directionY == 0 ? lateral : 0);
                    int y = target.tileY() + directionY * step
                        + (directionX == 0 ? lateral : 0);
                    Tile tile = Vars.world.tile(x, y);
                    if (tile == null) {
                        blockers++;
                    } else if (tile.build == target) {
                        continue;
                    } else if (tile.build != null || tile.solid()) {
                        blockers++;
                    }
                }
            }
            float distance = home.dst2(candidate.worldx(), candidate.worldy());
            if (blockers < fewestBlockers
                || (blockers == fewestBlockers && distance > greatestDistance)
                || (blockers == fewestBlockers && distance == greatestDistance && score > bestScore)) {
                best = candidate;
                fewestBlockers = blockers;
                greatestDistance = distance;
                bestScore = score;
            }
        }
        return best == null ? target.tile : best;
    }

    private void updateGuard() {
        if (action == Action.GUARD && worker.healthf() < 0.25f && home.isValid()) {
            action = Action.RETREAT;

            navigate(home.tile);
            phase = "生命过低，正在撤回己方核心";
            return;
        }
        if (!worker.within(destination.worldx(), destination.worldy(), Vars.tilesize * 2f)) {
            if (!controller().hasCommand()) navigate(destination);
            return;
        }
        if (action == Action.RETREAT) {
            finish("生命过低，已撤回核心");
            return;
        }
        guardTime -= Time.delta;
        phase = "正在守卫，剩余 " + (int)Math.ceil(guardTime / 60f) + " 秒";
        if (guardTime <= 0) {
            finish("已完成守卫，期间补给 " + guardAmmoDelivered + " 发弹药");
            return;
        }

        reloadCheck -= Time.delta;
        if (reloadCheck > 0 || !home.isValid()) return;
        reloadCheck = 120f;
        if (worker.stack.amount > 0) return;
        Building nearest = null;
        Item selectedAmmo = null;
        int selectedAvailable = 0;
        float distance = 20f * Vars.tilesize;
        for (Building building : worker.team.data().buildings) {
            if (!(building instanceof ItemTurretBuild candidate) || candidate.getAmmoFraction() > 0.25f) continue;
            Item ammo = null;
            for (Item item : Vars.content.items()) {
                if (home.items.get(item) > 0 && candidate.acceptStack(item, 1, worker) > 0) {
                    if (ammo == null || home.items.get(item) > home.items.get(ammo)) ammo = item;
                }
            }
            if (ammo == null) continue;
            float candidateDistance = candidate.dst(guardPost.worldx(), guardPost.worldy());
            if (candidateDistance < distance) {
                distance = candidateDistance;
                nearest = candidate;
                selectedAmmo = ammo;
                selectedAvailable = home.items.get(ammo);
            }
        }
        if (nearest == null) return;
        turret = nearest;
        cargo = selectedAmmo;
        remaining = turret.acceptStack(selectedAmmo, Math.min(10, selectedAvailable), worker);
        action = Action.RESUPPLY;
        navigate(worker.stack.amount >= remaining ? turret.tile : home.tile);
    }

    private void updateResupply() {
        if (guardPost != null && worker.healthf() < 0.25f && home.isValid()) {
            action = Action.RETREAT;
            navigate(home.tile);
            phase = "生命过低，正在撤回己方核心";
            return;
        }
        if (!turret.isValid() || turret.team != worker.team || !home.isValid() || home.team != worker.team) {
            completeResupply("补给目标或己方核心已失效");
            return;
        }
        if (destination == home.tile) {
            if (!worker.within(home, worker.type.range)) return;
            if (needsUnload(cargo) && !unloadCargo(home)) {
                completeResupply("核心没有足够空间卸下余料");
                return;
            }
            Call.takeItems(home, cargo, remaining - worker.stack.amount, worker);
            if (worker.stack.amount == 0) {
                completeResupply("核心物资库存不足");
                return;
            }
            navigate(turret.tile);
            return;
        }
        if (!worker.within(turret, worker.type.range)) return;
        int requested = Math.min(remaining, worker.stack.amount);
        int amount = turret.acceptStack(cargo, requested, worker);
        if (amount <= 0) {
            blockedTime += Time.delta;
            if (blockedTime >= 10f * 60f) {
                completeResupply("目标持续已满，已停止补给并保留剩余物资");
                return;
            }
            phase = "目标暂时已满，等待接收剩余 " + remaining + " " + cargo.name;
            return;
        }
        blockedTime = amount < requested ? blockedTime + Time.delta : 0;
        Call.transferItemTo(worker, cargo, amount, worker.x, worker.y, turret);
        delivered += amount;
        remaining = Math.max(0, remaining - amount);
        if (remaining == 0) {
            completeResupply("已送达 " + delivered + " " + cargo.name);
            return;
        }
        if (blockedTime >= 10f * 60f) {
            completeResupply("目标长期只接收少量物资，已停止补给并保留剩余物资");
            return;
        }
        if (worker.stack.amount == 0) worker.clearItem();
        if (guardPost != null) guardAmmoDelivered += amount;
        navigate(home.tile);
    }

    private void updateTransfer() {
        if (!transferSource.isValid() || transferSource.team != worker.team
            || !turret.isValid() || turret.team != worker.team) {
            finish("转运源建筑或目标建筑已失效");
            return;
        }
        if (destination == transferSource.tile) {
            if (!worker.within(transferSource, worker.type.range)) return;
            if (worker.stack.item == item && worker.stack.amount > 0) {
                navigate(turret.tile);
                phase = "携带 " + worker.stack.amount + " " + item.name + "，累计转运 " + delivered;
                return;
            }
            int capacity = Math.min(remaining - delivered, worker.type.itemCapacity - worker.stack.amount);
            if (capacity <= 0) {
                finish("陪玩单位背包已满，无法继续装载 " + item.name);
                return;
            }
            int previous = worker.stack.amount;
            Call.takeItems(transferSource, item, capacity, worker);
            int loaded = worker.stack.amount - previous;
            if (loaded <= 0) {
                finish("源建筑当前没有可转运的 " + item.name);
                return;
            }
            blockedTime = 0;
            navigate(turret.tile);
            phase = "已装载 " + loaded + " " + item.name + "，累计转运 " + delivered;
            return;
        }
        if (!worker.within(turret, worker.type.range)) return;
        int requested = Math.min(worker.stack.amount, remaining - delivered);
        int amount = turret.acceptStack(item, requested, worker);
        if (amount <= 0) {
            blockedTime += Time.delta;
            if (blockedTime >= 10f * 60f) {
                finish("目标持续已满，已中止转运并保留 " + worker.stack.amount + " " + item.name);
                return;
            }
            phase = "目标暂时已满，等待接收剩余 " + (remaining - delivered) + " " + item.name;
            return;
        }
        blockedTime = amount < requested ? blockedTime + Time.delta : 0;
        int carried = worker.stack.amount;
        Call.transferItemTo(worker, item, amount, worker.x, worker.y, turret);
        int moved = carried - worker.stack.amount;
        delivered += moved;
        if (delivered >= remaining) {
            finish("已转运 " + delivered + " " + item.name + " 到 " + turret.block.name
                + "@" + turret.tileX() + "," + turret.tileY());
            return;
        }
        if (blockedTime >= 10f * 60f) {
            finish("目标长期只接收少量 " + item.name + "，已中止转运并保留余料");
            return;
        }
        if (worker.stack.amount > 0) {
            phase = "目标暂时已满，等待接收剩余 " + worker.stack.amount + " " + item.name;
            return;
        }
        worker.clearItem();
        navigate(transferSource.tile);
        phase = "已送达 " + moved + " " + item.name + "，累计转运 " + delivered;
    }

    private void completeResupply(String result) {
        if (guardPost == null) {
            finish(result);
            return;
        }

        turret = null;
        action = Action.GUARD;
        navigate(guardPost);
    }

    private void verifySupply() {
        if (!supplyCore.isValid()) {
            finish("产线已建造，但核心已失效");
            return;
        }
        int inventory = supplyCore.items.get(supplyItem);
        if (inventory > supplyBaseline) {
            finish("已完工，共建造 " + completedBuildings + " 处；核心 "
                + supplyItem.name + " 库存 " + supplyBaseline + " → " + inventory);
        }
    }

    private void nextPlacement() {
        plan = construction.removeFirst();
        buildingQueued = false;
        navigate(Vars.world.tile(plan.x, plan.y));
    }

    private void updateBuild() {
        if (buildingQueued) return;
        if (!Build.validPlace(plan.block, worker.team, plan.x, plan.y, plan.rotation)) {
            finish("失败：建造位置已变化");
            return;
        }
        if (!worker.within(destination.worldx(), destination.worldy(), worker.type.buildRange * 0.8f)) return;
        controller().clearCommands();
        worker.addBuild(plan);
        buildingQueued = true;
        phase = "施工中，已完成 " + completedBuildings + " 处";
    }

    private void updateMining() {
        if (!depositCore.isValid()) {
            finish("失败：目标核心已失效，已运入 " + delivered);
            return;
        }
        if (miningReturn) {
            if (!worker.within(depositCore, worker.type.range)) return;
            int amount = depositCore.acceptStack(item, worker.stack.amount, worker);
            if (amount <= 0) {
                finish("未完成：核心库存已满，已运入 " + delivered);
                return;
            }
            transferToCore(depositCore, item, amount);
            remaining = Math.max(0, remaining - amount);
            delivered += amount;
            if (remaining == 0) {
                finish("已运入核心 " + delivered + " " + item.name);
                return;
            }
            miningReturn = false;
            navigateOre();
            return;
        }
        if (worker.stack.amount >= Math.min(remaining, Math.max(1, worker.type.itemCapacity - 1))) {
            worker.mineTile(null);
            miningReturn = true;
            navigateCore();
            return;
        }
        if (!worker.validMine(ore, false)) {
            ore = findOre(item);
            if (ore == null) {
                finish("失败：矿点已不可用，已运入 " + delivered);
                return;
            }
            navigateOre();
            return;
        }
        if (worker.validMine(ore) == false) {
            miningRetry -= Time.delta;
            if (miningRetry <= 0) navigateOre();
            return;
        }
        miningRetry = 0;
        controller().clearCommands();
        worker.mineTile(ore);
        phase = "采集中，累计入库 " + delivered;
    }

    private void navigateOre() {
        miningRetry = 60f;
        if (ore.solid() == false) {
            navigate(ore);
            return;
        }

        Tile approach = null;
        float closest = Float.MAX_VALUE;
        for (int radius = 1; radius <= 3 && approach == null; radius++) {
            for (int x = ore.x - radius; x <= ore.x + radius; x++) {
                for (int y = ore.y - radius; y <= ore.y + radius; y++) {
                    if (Math.abs(x - ore.x) != radius && Math.abs(y - ore.y) != radius) continue;
                    Tile candidate = Vars.world.tile(x, y);
                    if (candidate == null || candidate.solid()) continue;
                    float distance = candidate.dst2(worker.x, worker.y);
                    if (distance < closest) {
                        closest = distance;
                        approach = candidate;
                    }
                }
            }
        }
        navigate(approach == null ? ore : approach);
    }

    private Tile findOre(Item target) {
        Tile floorOre = worker.type.mineFloor ? Vars.indexer.findClosestOre(worker, target) : null;
        Tile wallOre = worker.type.mineWalls ? Vars.indexer.findClosestWallOre(worker, target) : null;
        if (floorOre == null) return wallOre;
        if (wallOre == null) return floorOre;
        return floorOre.dst2(worker.x, worker.y) <= wallOre.dst2(worker.x, worker.y) ? floorOre : wallOre;
    }

    private void transferToCore(Building core, Item transferred, int amount) {
        for (int index = 0; index < amount; index++) core.handleItem(core, transferred);
        if (Vars.headless && Vars.state.isCampaign()) transferred.unlock();
        worker.stack.amount -= amount;
        if (worker.stack.amount == 0) worker.clearItem();
    }

    private void navigateCore() {
        navigate(depositCore.tile);
        phase = "返回核心运料，累计入库 " + delivered;
    }

    private void navigate(Tile tile) {
        destination = tile;
        controller().commandPosition(new Vec2(tile.worldx(), tile.worldy()));
        phase = "正在前往 " + tile.x + "," + tile.y;
    }

    private void ensureNavigation() {
        if (destination == null || controller().hasCommand()) return;
        controller().commandPosition(new Vec2(destination.worldx(), destination.worldy()));
    }

    private boolean summon(Building core) {
        if (ready()) return worker.team == core.team;
        var type = ((CoreBlock)core.block).unitType;
        if (type == null || type.buildSpeed <= 0 || !Units.canCreate(core.team, type)) return false;
        worker = type.spawn(core.team, core.x, core.y);
        worker.controller(new CommandAI());
        return true;
    }

    private CommandAI controller() {
        if (worker.controller() instanceof CommandAI) return (CommandAI)worker.controller();
        CommandAI controller = new CommandAI();
        worker.controller(controller);
        return controller;
    }

    private boolean ready() {
        return worker != null && !worker.dead() && worker.isAdded();
    }

    private void start(Action kind, String description, int seconds) {
        action = kind;
        task = "#" + (++sequence) + " " + description;
        timeLeft = seconds * 60f;
        blockedTime = 0;
    }

    private void finish(String result) {
        action = null;
        phase = result;
        completedTasks.put(sequence, status());
        if (completedTasks.size() > 32) completedTasks.remove(completedTasks.keySet().iterator().next());
        if (ready()) {
            worker.mineTile(null);
            controller().clearCommands();
            // Build completion fires inside the engine's build queue update.
            Unit finishedWorker = worker;
            arc.Core.app.post(() -> { if (action != Action.BUILD) finishedWorker.clearBuilding(); });
        }
        clearTargets();
    }

    private void clearTargets() {
        destination = null;
        plan = null;
        construction.clear();
        supplyCore = null;
        supplyItem = null;
        home = null;
        turret = null;
        transferSource = null;
        guardPost = null;
        attackTarget = null;
        attackStagingTime = 0;
        for (Unit attacker : attackers) {
            if (attacker.isValid() && !attacker.isPlayer()
                && attacker.controller() instanceof CommandAI combat) combat.clearCommands();
        }
        attackers.clear();
        ore = null;
        item = null;
        depositCore = null;
        delivered = 0;
        buildingQueued = false;
    }

    private String accepted() {
        return "已受理 " + task + "。可查询状态获取进展。";
    }

    String busy() {
        return "已有任务在执行：" + status();
    }
}
