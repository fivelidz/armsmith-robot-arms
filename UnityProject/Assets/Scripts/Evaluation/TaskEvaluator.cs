using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith.Evaluation
{
    /// <summary>
    /// EV1 — builds the composable success PREDICATE TREE for each scenario, and a helper to snapshot the
    /// live scene into a TaskContext. ScenarioManager delegates its success test here so success logic lives
    /// in ONE declarative place (and is unit-testable headless without any MonoBehaviour).
    ///
    /// Object naming convention used in the predicate trees (resolved by the TaskContext built in
    /// ScenarioManager): "cube", "cubeB", "pad", "trayB", "bin", "reachTarget", and "sortCube{i}".
    /// </summary>
    public static class TaskEvaluator
    {
        // Tolerances kept identical to the legacy inline switch so behaviour is preserved exactly.
        const float kReachTol = 0.04f;
        const float kPadTol = 0.06f;
        const float kTrayTol = 0.06f;
        const float kBinTol = 0.06f;
        const float kStackXZ = 0.03f, kStackDy = 0.04f;
        const float kSortTol = 0.07f, kSortLowY = 0.07f;
        const float kSetDownY = 0.07f;

        /// <summary>The predicate tree whose satisfaction == task success for the given scenario.
        /// `sortCubeNames` lists the active SortIntoTray cubes (e.g. ["sortCube0","sortCube1","sortCube2"]).</summary>
        public static IPredicate Build(ArmSmith.ScenarioType type, IReadOnlyList<string> sortCubeNames)
        {
            switch (type)
            {
                case ArmSmith.ScenarioType.ReachTouch:
                    return new EeReaches("reachTarget", kReachTol);

                case ArmSmith.ScenarioType.PushToZone:
                case ArmSmith.ScenarioType.PickPlaceCube:
                    return new And(
                        new NearXZ("cube", "pad", kPadTol),
                        new AtRest("cube"));

                case ArmSmith.ScenarioType.TrayToTray:
                    return new And(
                        new NearXZ("cube", "trayB", kTrayTol),
                        new BelowHeight("cube", kSetDownY),
                        new AtRest("cube"));

                case ArmSmith.ScenarioType.DropInBin:
                    return new And(
                        new NearXZ("cube", "bin", kBinTol),
                        new BelowHeight("cube", 0.05f),
                        new AtRest("cube"));

                case ArmSmith.ScenarioType.StackTwo:
                    return new And(
                        new AboveAligned("cube", "cubeB", kStackDy, kStackXZ),
                        new AtRest("cube"));

                case ArmSmith.ScenarioType.SortIntoTray:
                    return new And(
                        new ForAll(sortCubeNames,
                            name => new And(
                                new NearXZ(name, "trayB", kSortTol),
                                new BelowHeight(name, kSortLowY)),
                            "scattered cubes inside the green tray"),
                        new AtRest("cube"));  // proxy rest gate (matches legacy Rest())

                default:
                    return new EeReaches("reachTarget", kReachTol);
            }
        }
    }
}
