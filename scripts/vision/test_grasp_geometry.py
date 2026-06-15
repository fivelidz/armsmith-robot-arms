"""
Unit tests for grasp_geometry.py — the CPU-side geometry of the CV grasp pipeline.
Run: python3 scripts/vision/test_grasp_geometry.py   (exit 0 = all pass)

These run with NO GPU and NO models — they validate the pure-numpy math against synthetic
point clouds, so the geometry is trustworthy before DA3/SAM3 are wired up. Suitable as a
regression gate (mirrors the style of scripts/run_checks.sh).
"""

import sys
import numpy as np

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from grasp_geometry import (  # noqa: E402
    euclidean_distance,
    angle_between_vectors,
    rotation_matrix_from_vectors,
    transform_points,
    project_point_to_camera,
    fit_ground_plane_ransac,
    masked_centroid,
    grasp_pose_from_observation,
)

_fails = []


def check(name, cond, extra=""):
    if cond:
        print(f"  [PASS] {name}")
    else:
        print(f"  [FAIL] {name} {extra}")
        _fails.append(name)


def synthetic_scene(cube_xyz=(0.10, 0.05, 0.30), cube_half=0.04, h=64, w=64, tilt=0.0):
    """Table at y=0 (optionally tilted about x by `tilt` rad) with a raised cube region."""
    xs = np.linspace(-0.3, 0.3, w)
    zs = np.linspace(0.0, 0.6, h)
    gx, gz = np.meshgrid(xs, zs)
    gy = np.tan(tilt) * gz  # tilt the plane
    pts = np.stack([gx, gy, gz], axis=-1)
    mask = (np.abs(gx - cube_xyz[0]) < cube_half) & (
        np.abs(gz - cube_xyz[2]) < cube_half
    )
    pts[mask, 1] = gy[mask] + cube_xyz[1]
    conf = np.ones((h, w))
    return pts, mask, conf


def test_basics():
    check(
        "euclidean_distance", abs(euclidean_distance([0, 0, 0], [3, 4, 0]) - 5.0) < 1e-9
    )
    check(
        "angle_between_vectors 90deg",
        abs(angle_between_vectors([1, 0, 0], [0, 1, 0]) - 90.0) < 1e-6,
    )
    check(
        "angle_between_vectors 0deg",
        abs(angle_between_vectors([1, 0, 0], [2, 0, 0])) < 1e-6,
    )


def test_rotation():
    R = rotation_matrix_from_vectors([0, 0, 1], [0, 1, 0])
    v = R @ np.array([0, 0, 1.0])
    check(
        "rotation maps z->y", np.allclose(v, [0, 1, 0], atol=1e-9), str(np.round(v, 4))
    )
    check("rotation is orthonormal", np.allclose(R @ R.T, np.eye(3), atol=1e-9))
    # antiparallel edge case
    R2 = rotation_matrix_from_vectors([0, 1, 0], [0, -1, 0])
    v2 = R2 @ np.array([0, 1.0, 0])
    check(
        "rotation antiparallel",
        np.allclose(v2, [0, -1, 0], atol=1e-6),
        str(np.round(v2, 4)),
    )


def test_transform():
    # translate by (1,2,3)
    M = np.eye(4)
    M[:3, 3] = [1, 2, 3]
    out = transform_points(np.array([[0.0, 0, 0], [1, 1, 1]]), M)
    check("transform_points translate", np.allclose(out, [[1, 2, 3], [2, 3, 4]]))


def test_projection():
    # camera at origin looking down +z (c2w = identity); point straight ahead projects to (cx,cy)
    c2w = np.eye(4)
    uv = project_point_to_camera([0, 0, 2.0], c2w, fx=500, fy=500, cx=320, cy=240)
    check(
        "project center",
        uv is not None and abs(uv[0] - 320) < 1e-6 and abs(uv[1] - 240) < 1e-6,
        str(uv),
    )
    behind = project_point_to_camera([0, 0, -1.0], c2w, fx=500, fy=500, cx=320, cy=240)
    check("project behind -> None", behind is None)


def test_plane_fit():
    pts, _, conf = synthetic_scene()
    normal, mask = fit_ground_plane_ransac(pts, conf)
    check("plane normal found", normal is not None)
    if normal is not None:
        n = normal if normal[1] >= 0 else -normal
        check(
            "plane normal ~ +Y",
            np.allclose(n, [0, 1, 0], atol=0.05),
            str(np.round(n, 4)),
        )


def test_centroid():
    pts, mask, conf = synthetic_scene(cube_xyz=(0.1, 0.05, 0.3))
    c, n, mc = masked_centroid(pts, mask, conf)
    check("centroid found", c is not None)
    if c is not None:
        check(
            "centroid x,z ~ cube",
            abs(c[0] - 0.1) < 0.02 and abs(c[2] - 0.3) < 0.02,
            str(np.round(c, 3)),
        )
        check("centroid y ~ cube top", abs(c[1] - 0.05) < 0.005, str(np.round(c, 3)))
        check("centroid n_points > 0", n > 0)


def test_full_pipeline_flat():
    pts, mask, conf = synthetic_scene()
    g = grasp_pose_from_observation(pts, mask, conf)
    check("grasp pose returned", g is not None)
    if g is not None:
        check(
            "grasp pos ~ cube",
            np.allclose(g.position, [0.1, 0.05, 0.3], atol=0.02),
            str(np.round(g.position, 3)),
        )
        check(
            "approach down (-Y)",
            np.allclose(g.approach_dir, [0, -1, 0], atol=0.05),
            str(np.round(g.approach_dir, 3)),
        )
        check("support up (+Y)", g.support_normal[1] > 0.9)


def test_full_pipeline_tilted():
    # a tilted table -> the approach direction should tilt with the surface normal
    pts, mask, conf = synthetic_scene(tilt=0.20)  # ~11.5 deg tilt
    g = grasp_pose_from_observation(pts, mask, conf)
    check("tilted grasp returned", g is not None)
    if g is not None:
        # approach should be opposite the (upward) surface normal, no longer perfectly vertical
        ang_from_down = angle_between_vectors(g.approach_dir, [0, -1, 0])
        check(
            "tilted approach follows surface (5..20 deg)",
            5.0 < ang_from_down < 20.0,
            f"{ang_from_down:.1f}deg",
        )
        # round-trip: rotation should map the gripper axis onto approach_dir
        mapped = g.rotation @ np.array([0, -1.0, 0])
        check(
            "rotation maps gripper->approach",
            np.allclose(mapped, g.approach_dir, atol=1e-6),
            str(np.round(mapped, 4)),
        )


def test_empty_mask():
    pts, _, conf = synthetic_scene()
    empty = np.zeros(pts.shape[:2], bool)
    g = grasp_pose_from_observation(pts, empty, conf)
    check("empty mask -> None", g is None)


if __name__ == "__main__":
    print("=== grasp_geometry unit tests ===")
    for fn in [
        test_basics,
        test_rotation,
        test_transform,
        test_projection,
        test_plane_fit,
        test_centroid,
        test_full_pipeline_flat,
        test_full_pipeline_tilted,
        test_empty_mask,
    ]:
        print(f"[{fn.__name__}]")
        fn()
    print(
        f"=== RESULT: {'ALL PASS' if not _fails else str(len(_fails)) + ' FAILED: ' + ', '.join(_fails)} ==="
    )
    sys.exit(1 if _fails else 0)
