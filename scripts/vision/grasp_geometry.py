"""
grasp_geometry.py — standalone, dependency-free (numpy-only) 3D geometry toolbox for the
ARMSMITH computer-vision / spatial-AI grasp pipeline.

WHY THIS EXISTS
---------------
The CV pipeline (HANDOVER.md §8 item 2) turns a camera frame into a 6-DOF grasp pose:

    WristCam RGB --> SAM3.detect("the cube")        --> object mask
    WristCam RGB --> DepthAnything3.reconstruct     --> points[H,W,3] (arm-base frame) + confidence
    mask + points + confidence --> 3D centroid (grasp position)
    points --> RANSAC ground plane --> support normal --> gripper approach orientation
    (centroid, orientation) --> hand to ARMSMITH's analytic IK as the end-effector target

The heavy perception models (DA3, SAM3) run on the GPU server (serve via ROCm — this machine
has a Radeon 8060S / gfx1151 with ~75 GB usable unified memory and PyTorch 2.10 + HIP 7.2).
This module is the *CPU side* — the pure-numpy geometry that converts the server's depth/points/
mask output into a grasp pose. It has NO torch / GPU / SpatialClaw dependencies, so it runs
anywhere and is unit-testable against Unity ground-truth point clouds before any model is wired up.

The core math (rotation_matrix_from_vectors, fit_ground_plane_ransac, transform_points,
project_point_to_camera) is ported faithfully from NVLABS SpatialClaw's geometry_utils.py
(spatial_agent/tools/geometry_utils.py) — flagged in the research as "copy-pasteable". The
SpatialClaw original is NC-licensed research code; this is a clean reimplementation for our
research project.

UNITS / FRAMES
--------------
ARMSMITH works in METRES (sim-to-real). All points/positions here are metres in the arm-base
frame unless noted. DA3 returns metric depth; because the sim is already metric, scale
calibration is a non-issue.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional, Tuple

import numpy as np


# --------------------------------------------------------------------------------------
# Basic vector / point helpers
# --------------------------------------------------------------------------------------
def euclidean_distance(p1, p2) -> float:
    """3D Euclidean distance between two points (metres)."""
    p1 = np.asarray(p1, dtype=np.float64).ravel()
    p2 = np.asarray(p2, dtype=np.float64).ravel()
    if p1.shape != p2.shape:
        raise ValueError(f"p1 shape {p1.shape} and p2 shape {p2.shape} must match.")
    return float(np.linalg.norm(p1 - p2))


def angle_between_vectors(v1, v2) -> float:
    """Angle in DEGREES between two 3-vectors."""
    v1 = np.asarray(v1, dtype=np.float64).ravel()
    v2 = np.asarray(v2, dtype=np.float64).ravel()
    n1, n2 = np.linalg.norm(v1), np.linalg.norm(v2)
    if n1 < 1e-12 or n2 < 1e-12:
        raise ValueError(
            f"Cannot angle a zero-length vector. ||v1||={n1:.2e} ||v2||={n2:.2e}"
        )
    cos_angle = np.dot(v1, v2) / (n1 * n2)
    return float(np.degrees(np.arccos(np.clip(cos_angle, -1.0, 1.0))))


def rotation_matrix_from_vectors(v_from, v_to) -> np.ndarray:
    """Rotation matrix (3,3) that rotates ``v_from`` onto ``v_to`` (Rodrigues).

    Inputs are (3,) vectors (need not be unit). Handles the (anti)parallel edge cases.
    Used to align the gripper's approach axis with the (negated) support-surface normal.
    """
    a = np.asarray(v_from, dtype=np.float64)
    b = np.asarray(v_to, dtype=np.float64)
    a = a / (np.linalg.norm(a) + 1e-12)
    b = b / (np.linalg.norm(b) + 1e-12)
    v = np.cross(a, b)
    c = float(np.dot(a, b))
    if np.linalg.norm(v) < 1e-8:
        if c > 0:
            return np.eye(3)
        # 180-degree rotation about any axis perpendicular to a
        perp = (
            np.array([1.0, 0.0, 0.0]) if abs(a[0]) < 0.9 else np.array([0.0, 1.0, 0.0])
        )
        perp = perp - np.dot(perp, a) * a
        perp = perp / np.linalg.norm(perp)
        return 2.0 * np.outer(perp, perp) - np.eye(3)
    vx = np.array(
        [
            [0.0, -v[2], v[1]],
            [v[2], 0.0, -v[0]],
            [-v[1], v[0], 0.0],
        ]
    )
    return np.eye(3) + vx + vx @ vx / (1.0 + c)


def transform_points(points: np.ndarray, matrix: np.ndarray) -> np.ndarray:
    """Apply a (4,4) SE(3) matrix to (...,3) points; returns same shape."""
    points = np.asarray(points, dtype=np.float64)
    if points.shape[-1] != 3:
        raise ValueError(f"points last dim must be 3, got {points.shape}.")
    matrix = np.asarray(matrix, dtype=np.float64)
    if matrix.shape != (4, 4):
        raise ValueError(f"matrix must be (4,4), got {matrix.shape}.")
    orig = points.shape
    flat = points.reshape(-1, 3)
    homo = np.hstack([flat, np.ones((flat.shape[0], 1))])
    out = (matrix @ homo.T).T[:, :3]
    return out.reshape(orig)


def project_point_to_camera(
    point_3d, extrinsic_c2w, fx, fy, cx, cy
) -> Optional[Tuple[float, float]]:
    """Project a 3D world point to (u,v) pixels. Returns None if behind the camera."""
    point_3d = np.asarray(point_3d, dtype=np.float64).ravel()
    extrinsic_c2w = np.asarray(extrinsic_c2w, dtype=np.float64)
    if extrinsic_c2w.shape != (4, 4):
        raise ValueError(f"extrinsic_c2w must be (4,4), got {extrinsic_c2w.shape}.")
    w2c = np.linalg.inv(extrinsic_c2w)
    p_cam = w2c @ np.append(point_3d, 1.0)
    if p_cam[2] <= 0:
        return None
    u = fx * p_cam[0] / p_cam[2] + cx
    v = fy * p_cam[1] / p_cam[2] + cy
    return (float(u), float(v))


# --------------------------------------------------------------------------------------
# Plane fitting (support surface)
# --------------------------------------------------------------------------------------
def fit_ground_plane_ransac(
    points: np.ndarray,
    confidence: Optional[np.ndarray] = None,
    conf_threshold: float = 0.3,
    n_iterations: int = 1000,
    inlier_threshold: float = 0.05,
    seed: int = 42,
) -> Tuple[Optional[np.ndarray], Optional[np.ndarray]]:
    """RANSAC plane fit. Returns (plane_normal (3,), inlier_mask) or (None, None).

    points: (H,W,3) or (N,3) in metres. confidence: matching shape minus last dim
    (defaults to all-ones if None). The normal SIGN is ambiguous — the caller
    disambiguates with a known "up" reference (see grasp_pose_from_observation).
    """
    pts = np.asarray(points, dtype=np.float64).reshape(-1, 3)
    if confidence is None:
        conf = np.ones(pts.shape[0])
    else:
        conf = np.asarray(confidence, dtype=np.float64).reshape(-1)
    mask = conf > conf_threshold
    valid = pts[mask]
    if len(valid) < 100:
        return None, None

    best_normal, best_inliers, best_mask = None, 0, None
    rng = np.random.default_rng(seed)
    for _ in range(n_iterations):
        idx = rng.choice(len(valid), size=3, replace=False)
        p0, p1, p2 = valid[idx]
        normal = np.cross(p1 - p0, p2 - p0)
        norm = np.linalg.norm(normal)
        if norm < 1e-8:
            continue
        normal /= norm
        dists = np.abs((valid - p0) @ normal)
        inl = dists < inlier_threshold
        n = int(inl.sum())
        if n > best_inliers:
            best_inliers, best_normal, best_mask = n, normal, inl
    if best_normal is None or best_inliers < 50:
        return None, None

    # least-squares refit from inliers (SVD)
    inl_pts = valid[best_mask]
    centroid = inl_pts.mean(axis=0)
    _, _, vh = np.linalg.svd(inl_pts - centroid, full_matrices=False)
    return vh[-1], best_mask


# --------------------------------------------------------------------------------------
# Grasp-pose synthesis (the actual deliverable)
# --------------------------------------------------------------------------------------
@dataclass
class GraspPose:
    """A 6-DOF grasp suggestion in the arm-base frame (metres / rotation matrix)."""

    position: np.ndarray  # (3,) grasp point  (the object centroid)
    approach_dir: (
        np.ndarray
    )  # (3,) unit vector the gripper should approach ALONG (toward object)
    rotation: (
        np.ndarray
    )  # (3,3) rotation aligning gripper approach axis -> approach_dir
    support_normal: np.ndarray  # (3,) unit normal of the support surface (points "up")
    n_points: int  # number of object points used for the centroid
    confidence: float  # mean confidence of the points used (0..1)

    def as_dict(self) -> dict:
        return {
            "position": self.position.tolist(),
            "approach_dir": self.approach_dir.tolist(),
            "rotation": self.rotation.tolist(),
            "support_normal": self.support_normal.tolist(),
            "n_points": int(self.n_points),
            "confidence": float(self.confidence),
        }


def masked_centroid(
    points: np.ndarray,
    mask: np.ndarray,
    confidence: Optional[np.ndarray] = None,
) -> Tuple[Optional[np.ndarray], int, float]:
    """Confidence-weighted 3D centroid of the masked (segmented) object points.

    points: (H,W,3). mask: (H,W) bool/0-1 from SAM3. confidence: (H,W) from DA3 (optional).
    Returns (centroid (3,) | None, n_points, mean_conf).
    """
    pts = np.asarray(points, dtype=np.float64).reshape(-1, 3)
    m = np.asarray(mask).reshape(-1).astype(bool)
    if confidence is None:
        w = np.ones(pts.shape[0])
    else:
        w = np.asarray(confidence, dtype=np.float64).reshape(-1)
    sel = m & np.isfinite(pts).all(axis=1) & (w > 0)
    if sel.sum() == 0:
        return None, 0, 0.0
    p = pts[sel]
    wp = w[sel]
    centroid = (p * wp[:, None]).sum(axis=0) / wp.sum()
    return centroid, int(sel.sum()), float(wp.mean())


def grasp_pose_from_observation(
    points: np.ndarray,
    object_mask: np.ndarray,
    confidence: Optional[np.ndarray] = None,
    up_reference: np.ndarray = np.array([0.0, 1.0, 0.0]),
    gripper_approach_axis: np.ndarray = np.array([0.0, -1.0, 0.0]),
    conf_threshold: float = 0.3,
) -> Optional[GraspPose]:
    """Full pipeline: depth points + object mask -> 6-DOF grasp pose.

    Args
    ----
    points              : (H,W,3) metric point map in the ARM-BASE frame (from DA3, transformed).
    object_mask         : (H,W) bool/0-1 segmentation of the target object (from SAM3).
    confidence          : (H,W) per-pixel confidence (from DA3); optional.
    up_reference        : world "up" used to disambiguate the support-plane normal sign.
                          ARMSMITH arm-base frame: +Y is up.
    gripper_approach_axis: the gripper's local approach axis in its own frame; for a top-down
                          pick the gripper points DOWN (-Y), so we align -Y with the support
                          normal pointing toward the object.
    Returns GraspPose, or None if the object/plane could not be resolved.
    """
    centroid, n, conf = masked_centroid(points, object_mask, confidence)
    if centroid is None:
        return None

    # Support surface normal from the (non-object) scene; fall back to up_reference if RANSAC fails.
    normal, _ = fit_ground_plane_ransac(
        points, confidence, conf_threshold=conf_threshold
    )
    up = np.asarray(up_reference, dtype=np.float64)
    up = up / (np.linalg.norm(up) + 1e-12)
    if normal is None:
        support_normal = up
    else:
        # disambiguate sign so the normal points "up" (same hemisphere as up_reference)
        support_normal = normal if np.dot(normal, up) >= 0 else -normal

    # The gripper should approach the object along -support_normal (down onto the surface).
    approach_dir = -support_normal
    approach_dir = approach_dir / (np.linalg.norm(approach_dir) + 1e-12)

    rotation = rotation_matrix_from_vectors(gripper_approach_axis, approach_dir)

    return GraspPose(
        position=centroid,
        approach_dir=approach_dir,
        rotation=rotation,
        support_normal=support_normal,
        n_points=n,
        confidence=conf,
    )


if __name__ == "__main__":
    # tiny self-demo with a synthetic table + cube point cloud
    H = W = 64
    xs = np.linspace(-0.3, 0.3, W)
    zs = np.linspace(0.0, 0.6, H)
    gx, gz = np.meshgrid(xs, zs)
    table = np.stack([gx, np.zeros_like(gx), gz], axis=-1)  # y=0 plane
    pts = table.copy()
    mask = np.zeros((H, W), bool)
    # carve a cube region raised to y=0.05 around (0.1, 0.3)
    cube = (np.abs(gx - 0.1) < 0.04) & (np.abs(gz - 0.3) < 0.04)
    pts[cube, 1] = 0.05
    mask[cube] = True
    g = grasp_pose_from_observation(pts, mask)
    assert g is not None
    print("grasp position (expect ~[0.1, 0.05, 0.3]):", np.round(g.position, 3))
    print("support normal (expect ~[0,1,0]):", np.round(g.support_normal, 3))
    print("approach dir   (expect ~[0,-1,0]):", np.round(g.approach_dir, 3))
    print("n_points:", g.n_points, "conf:", round(g.confidence, 3))
    print("OK")
