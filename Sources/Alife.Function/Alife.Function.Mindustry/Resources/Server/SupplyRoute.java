package alife.mindustry;

import arc.struct.Seq;
import mindustry.Vars;
import mindustry.content.Blocks;
import mindustry.gen.Building;
import mindustry.entities.units.BuildPlan;
import mindustry.game.Team;
import mindustry.type.Item;
import mindustry.world.Build;
import mindustry.world.Tile;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

final class SupplyRoute {
    static List<BuildPlan> plan(Building core, Item item) {
        Tile ore = Vars.indexer.findClosestOre(core.x, core.y, item);
        if (ore == null) return List.of();
        for (int dx = 0; dx >= -1; dx--) {
            for (int dy = 0; dy >= -1; dy--) {
                Tile drill = Vars.world.tile(ore.x + dx, ore.y + dy);
                if (drill == null || !Build.validPlace(Blocks.mechanicalDrill, core.team, drill.x, drill.y, 0)) continue;
                Seq<Tile> footprint = drill.getLinkedTilesAs(Blocks.mechanicalDrill, new Seq<>());
                if (footprint.size != 4 || footprint.contains(tile -> tile.block() != Blocks.air
                    || !visible(core.team, tile) || (tile.drop() != null && tile.drop() != item))) continue;

                List<BuildPlan> line = connect(core, footprint);
                if (!line.isEmpty()) {
                    line.add(new BuildPlan(drill.x, drill.y, 0, Blocks.mechanicalDrill));
                    return line;
                }
            }
        }
        return List.of();
    }

    private static List<BuildPlan> connect(Building core, Seq<Tile> drill) {
        Map<Tile, Tile> towardCore = new HashMap<>();
        ArrayDeque<Tile> open = new ArrayDeque<>();
        for (Tile footprint : core.tile.getLinkedTiles(new Seq<>())) {
            for (int direction = 0; direction < 4; direction++) {
                Tile neighbor = footprint.nearby(direction);
                if (beltSite(core.team, neighbor, footprint, drill) && !towardCore.containsKey(neighbor)) {
                    towardCore.put(neighbor, footprint);
                    open.add(neighbor);
                }
            }
        }

        while (!open.isEmpty() && towardCore.size() < 4096) {
            Tile next = open.remove();
            for (int direction = 0; direction < 4; direction++) {
                Tile candidate = next.nearby(direction);
                if (candidate == null) continue;
                if (drill.contains(candidate, true)) return reconstruct(core, towardCore, next);
                if (!towardCore.containsKey(candidate) && beltSite(core.team, candidate, next, drill)) {
                    towardCore.put(candidate, next);
                    open.add(candidate);
                }
            }
        }
        return List.of();
    }

    private static boolean beltSite(Team team, Tile tile, Tile next, Seq<Tile> drill) {
        return tile != null && tile.block() == Blocks.air && !drill.contains(tile, true)
            && visible(team, tile) && Build.validPlace(Blocks.conveyor, team, tile.x, tile.y, tile.relativeTo(next));
    }

    private static boolean visible(Team team, Tile tile) {
        return !Vars.state.rules.fog || Vars.fogControl.isVisibleTile(team, tile.x, tile.y);
    }

    private static List<BuildPlan> reconstruct(Building core, Map<Tile, Tile> towardCore, Tile tile) {
        ArrayList<BuildPlan> line = new ArrayList<>();
        while (tile.build != core) {
            Tile next = towardCore.get(tile);
            line.add(new BuildPlan(tile.x, tile.y, tile.relativeTo(next), Blocks.conveyor));
            tile = next;
        }
        Collections.reverse(line);
        return line;
    }
}
