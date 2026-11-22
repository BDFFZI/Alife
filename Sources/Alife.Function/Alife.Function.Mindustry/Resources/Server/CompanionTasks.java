package alife.mindustry;

import arc.math.geom.Vec2;
import mindustry.Vars;
import mindustry.ai.types.CommandAI;
import mindustry.content.Items;
import mindustry.content.Blocks;
import mindustry.world.blocks.defense.turrets.ItemTurret.ItemTurretBuild;
import mindustry.entities.Units;
import mindustry.entities.units.BuildPlan;
import mindustry.gen.Building;
import mindustry.gen.Call;
import mindustry.gen.Unit;
import mindustry.type.Item;
import mindustry.world.Build;
import mindustry.world.Tile;
import mindustry.world.blocks.storage.CoreBlock;

import arc.util.Time;
import java.util.ArrayDeque;
import java.util.List;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;

final class CompanionTasks {
    private enum Action { MOVE, BUILD, MINE, VERIFY_SUPPLY, GUARD, RETREAT, RESUPPLY, ATTACK, COLLECT, UNLOAD }

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
    private Item cargo;
    private Building attackTarget;
    private Action afterUnload;
    private Unit attacker;
    private Tile guardPost;
    private float reloadCheck;
    private int guardCopperDelivered;
    private Item item;
    private Building depositCore;
    private Tile ore;
    private int remaining;
    private int delivered;
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
        return ready() ? worker.tileX() + "," + worker.tileY() + "，生命 " + Math.round(worker.health)
            + "/" + Math.round(worker.maxHealth) + (worker.stack.amount > 0
            ? "，携带 " + worker.stack.item.name + "=" + worker.stack.amount : "")
            : "将在首条有效指令后从己方核心出发";
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
        attacker = core.team.data().units.find(unit -> unit != worker && unit.isValid() && !unit.isPlayer()
            && unit.healthf() >= 0.25f && unit.controller() instanceof CommandAI
            && unit.type.weapons.contains(weapon -> weapon.bullet.collidesGround && weapon.bullet.buildingDamageMultiplier > 0));
        if (attacker == null) return "己方没有可用的攻城单位，请先生产可以伤害建筑的作战单位。";
        start(Action.ATTACK, "进攻 " + target.block.name + "@" + target.tileX() + "," + target.tileY(), 240);
        home = core;
        attackTarget = target;
        ((CommandAI)attacker.controller()).commandTarget(target);
        phase = "正在接敌";
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
        guardCopperDelivered = 0;
        navigate(target);
        return accepted();
    }

    String build(Building core, BuildPlan target) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        return buildQueue(target.block.name + "@" + target.x + "," + target.y, List.of(target), 90);
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
        if (!summon(core) || !worker.canMine() || !worker.canMine(target)) {
            return "陪玩单位无法直接开采 " + target.name + "。可尝试用机械钻头产线采集。";
        }

        Tile source = findOre(target);
        if (source == null) return "附近没有可开采的 " + target.name + "。";
        if (core.acceptStack(target, 1, worker) == 0) return "核心无法接收 " + target.name + "。";
        start(Action.MINE, "采运 " + amount + " " + target.name, 150);
        ore = source;
        item = target;
        remaining = amount;
        delivered = 0;
        depositCore = core;
        miningReturn = worker.stack.item == target && worker.stack.amount >= amount;
        if (needsUnload(target)) beginUnload(Action.MINE);
        else if (miningReturn) navigateCore();
        else navigate(ore);
        return accepted();
    }

    String resupply(Building core, Building target, Item material, int amount) {
        if (action != null) return busy();
        if (!summon(core)) return "该队伍没有可用的陪玩单位。";
        int carried = worker.stack.item == material ? worker.stack.amount : 0;
        int accepted = target.acceptStack(material, Math.min(amount, worker.type.itemCapacity), worker);
        if (accepted <= 0) return "目标已满或不能接收该物资。";
        if (core.items.get(material) + carried < accepted) return "核心可用物资不足。";

        start(Action.RESUPPLY, "物资补给 " + target.tileX() + "," + target.tileY(), 90);
        cargo = material;
        home = core;
        turret = target;
        remaining = accepted;
        if (carried >= remaining) navigate(turret.tile);
        else navigate(home.tile);
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
        navigate(action == Action.MINE ? ore : turret.tile);
    }

    private boolean unloadCargo(Building core) {
        Item previous = worker.stack.item;
        int amount = worker.stack.amount;
        if (core.acceptStack(previous, amount, worker) < amount) return false;
        Call.transferItemTo(worker, previous, amount, worker.x, worker.y, core);
        worker.clearItem();
        return true;
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
        Call.transferItemTo(worker, item, amount, worker.x, worker.y, depositCore);
        if (worker.stack.amount == 0) worker.clearItem();
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
                if (worker.within(destination.worldx(), destination.worldy(), Vars.tilesize)) finish("已到达");
            }
            case BUILD -> updateBuild();
            case MINE -> updateMining();
            case VERIFY_SUPPLY -> verifySupply();
            case GUARD, RETREAT -> updateGuard();
            case RESUPPLY -> updateResupply();
            case ATTACK -> updateAttack();
            case COLLECT -> updateCollection();
            case UNLOAD -> updateUnload();
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
        if (!attacker.isValid() || attacker.isPlayer() || attacker.team != home.team
            || !(attacker.controller() instanceof CommandAI combat)) {
            finish("作战单位已失效或已由玩家接管");
            return;
        }
        if (!attackTarget.isValid() || attackTarget.team == attacker.team) {
            finish("目标已移除或不再是敌方建筑");
            return;
        }
        if (attacker.healthf() < 0.25f) {
            if (home.isValid()) combat.commandPosition(new Vec2(home.x, home.y));
            attacker = null;
            finish("作战单位生命过低，已下达返航指令");
        }
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
            finish("已完成守卫，期间补给 " + guardCopperDelivered + " 铜");
            return;
        }

        reloadCheck -= Time.delta;
        if (reloadCheck > 0 || !home.isValid()) return;
        reloadCheck = 120f;
        if (worker.stack.amount > 0 && worker.stack.item != Items.copper) return;
        int available = home.items.get(Items.copper) + worker.stack.amount;
        if (available == 0) return;
        Building nearest = null;
        float distance = 20f * Vars.tilesize;
        for (Building building : worker.team.data().getBuildings(Blocks.duo)) {
            ItemTurretBuild candidate = (ItemTurretBuild)building;
            float candidateDistance = candidate.dst(guardPost.worldx(), guardPost.worldy());
            if (candidateDistance < distance && candidate.getAmmoFraction() <= 0.25f
                && candidate.acceptStack(Items.copper, 1, worker) > 0) {
                distance = candidateDistance;
                nearest = candidate;
            }
        }
        if (nearest == null) return;
        turret = nearest;
        cargo = Items.copper;
        remaining = turret.acceptStack(Items.copper, Math.min(10, available), worker);
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
        int amount = turret.acceptStack(cargo, Math.min(remaining, worker.stack.amount), worker);
        if (amount <= 0) {
            completeResupply("目标已无法接收物资；余料仍在陪玩单位背包");
            return;
        }
        Call.transferItemTo(worker, cargo, amount, worker.x, worker.y, turret);
        if (worker.stack.amount == 0) worker.clearItem();
        if (guardPost != null) guardCopperDelivered += amount;
        completeResupply("已送达 " + amount + " " + cargo.name);
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
            Call.transferItemTo(worker, item, amount, worker.x, worker.y, depositCore);

            if (worker.stack.amount == 0) worker.clearItem();
            remaining = Math.max(0, remaining - amount);
            delivered += amount;
            if (remaining == 0) {
                finish("已运入核心 " + delivered + " " + item.name);
                return;
            }
            miningReturn = false;
            navigate(ore);
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
            navigate(ore);
        }
        if (worker.validMine(ore)) {
            controller().clearCommands();
            worker.mineTile(ore);
            phase = "采集中，累计入库 " + delivered;
        }
    }

    private Tile findOre(Item target) {
        return worker.type.mineFloor ? Vars.indexer.findClosestOre(worker, target) : null;
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

    private boolean summon(Building core) {
        if (ready()) return worker.team == core.team;
        var type = ((CoreBlock)core.block).unitType;
        if (type == null || type.buildSpeed <= 0 || !Units.canCreate(core.team, type)) return false;
        worker = type.spawn(core.team, core.x, core.y);
        worker.controller(new CommandAI());
        return true;
    }

    private CommandAI controller() {
        return (CommandAI)worker.controller();
    }

    private boolean ready() {
        return worker != null && !worker.dead() && worker.isAdded() && worker.controller() instanceof CommandAI;
    }

    private void start(Action kind, String description, int seconds) {
        action = kind;
        task = "#" + (++sequence) + " " + description;
        timeLeft = seconds * 60f;
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
        guardPost = null;
        attackTarget = null;
        if (attacker != null && attacker.isValid() && !attacker.isPlayer()
            && attacker.controller() instanceof CommandAI combat) combat.clearCommands();
        attacker = null;
        ore = null;
        item = null;
        depositCore = null;
        buildingQueued = false;
    }

    private String accepted() {
        return "已受理 " + task + "。可查询状态获取进展。";
    }

    private String busy() {
        return "已有任务在执行：" + status();
    }
}
