#!/usr/bin/env python3
"""
train_diffusion_policy.py — DF3: train a low-dim joint-space Diffusion Policy for ARMSMITH.

Consumes the portable intermediate dataset produced by scripts/realbot/waypoints_to_lerobot.py
(manifest.json + episodes/episode_XXXX.json) and trains a small conditional action-diffusion model
(the Chi et al. 2023 Diffusion Policy recipe, low-dim variant — research/diffusion_pathfinding/REPORT.md):

    observation = stack of recent joint states (proprioception)
    action      = chunk of future joint+gripper targets (degrees)
    model       = 1D conditional denoiser (MLP/temporal); DDPM training, DDIM-ish sampling at inference

Two backends, lazy-imported so the script is inspectable on ANY machine:
  • --backend torch   : a SELF-CONTAINED minimal PyTorch DDPM trainer (no LeRobot needed). Reference
                        implementation good for a first end-to-end run on the low-dim data.
  • --backend lerobot : delegate to LeRobot's maintained DiffusionPolicy (recommended for real training;
                        prints the exact `lerobot-train` invocation and the dataset mapping).

Default --dry-run just validates the dataset + prints the training plan (works with no ML deps).

Usage:
  python3 train_diffusion_policy.py dataset/ --dry-run
  python3 train_diffusion_policy.py dataset/ --backend torch --epochs 200 -o ckpt/
  python3 train_diffusion_policy.py dataset/ --backend lerobot --repo-id you/armsmith_pickplace
"""

import argparse, json, math, os, sys, glob


# ----------------------------- dataset --------------------------------------------------------------
def load_dataset(path):
    """Load the portable intermediate dataset. Returns (manifest, episodes[list of dict])."""
    man_path = os.path.join(path, "manifest.json")
    if not os.path.exists(man_path):
        raise FileNotFoundError(
            f"{man_path} not found — run waypoints_to_lerobot.py first."
        )
    manifest = json.load(open(man_path))
    eps = []
    for fp in sorted(glob.glob(os.path.join(path, "episodes", "episode_*.json"))):
        eps.append(json.load(open(fp)))
    if not eps:
        raise ValueError("no episodes found in dataset/episodes/")
    return manifest, eps


def make_windows(eps, obs_steps, pred_horizon):
    """Slice episodes into (obs_window, action_chunk) training pairs (the Diffusion Policy recipe)."""
    obs, act = [], []
    for ep in eps:
        A = ep["action"]
        S = ep.get("observation.state", A)
        T = len(A)
        for t in range(T):
            o = [
                S[max(0, t - k)] for k in range(obs_steps - 1, -1, -1)
            ]  # last obs_steps states
            a = [
                A[min(T - 1, t + h)] for h in range(pred_horizon)
            ]  # next pred_horizon actions
            obs.append(o)
            act.append(a)
    return obs, act


# ----------------------------- torch backend (self-contained DDPM) ----------------------------------
def train_torch(manifest, eps, args):
    try:
        import torch, torch.nn as nn
    except Exception as e:
        print(
            f"[torch] PyTorch not available ({e}). `pip install torch` to use --backend torch."
        )
        return 1
    import numpy as np

    dim = manifest["action_dim"]
    obs_steps, H = args.obs_steps, args.horizon
    obs, act = make_windows(eps, obs_steps, H)
    stats = manifest["stats"]
    mean = np.array(stats["mean"], dtype=np.float32)
    std = np.maximum(np.array(stats["std"], dtype=np.float32), 1e-3)

    def norm(x):
        return (np.array(x, dtype=np.float32) - mean) / std

    O = torch.tensor(
        np.stack([np.concatenate([norm(s) for s in o]) for o in obs])
    )  # [N, obs_steps*dim]
    A = torch.tensor(
        np.stack([np.stack([norm(a) for a in chunk]) for chunk in act])
    )  # [N, H, dim]
    N = O.shape[0]
    print(f"[torch] {N} windows | obs={O.shape[1]} action_chunk={H}x{dim}")

    Tdiff = args.diffusion_steps
    betas = torch.linspace(1e-4, 0.02, Tdiff)
    alphas = torch.cumprod(1 - betas, 0)

    class Denoiser(nn.Module):
        def __init__(self, cond, ad):
            super().__init__()
            self.net = nn.Sequential(
                nn.Linear(cond + ad + 1, 256),
                nn.SiLU(),
                nn.Linear(256, 256),
                nn.SiLU(),
                nn.Linear(256, ad),
            )

        def forward(self, a_noisy, cond, k):
            return self.net(torch.cat([a_noisy, cond, k], -1))

    ad = H * dim
    model = Denoiser(O.shape[1], ad)
    opt = torch.optim.Adam(model.parameters(), lr=args.lr)
    Aflat = A.reshape(N, ad)

    for ep in range(args.epochs):
        idx = torch.randperm(N)
        tot = 0.0
        for s in range(0, N, args.batch):
            b = idx[s : s + args.batch]
            a0 = Aflat[b]
            cond = O[b]
            k = torch.randint(0, Tdiff, (a0.shape[0],))
            ac = alphas[k].unsqueeze(-1)
            noise = torch.randn_like(a0)
            a_noisy = torch.sqrt(ac) * a0 + torch.sqrt(1 - ac) * noise
            pred = model(a_noisy, cond, k.float().unsqueeze(-1) / Tdiff)
            loss = ((pred - noise) ** 2).mean()
            opt.zero_grad()
            loss.backward()
            opt.step()
            tot += loss.item() * a0.shape[0]
        if ep % max(1, args.epochs // 10) == 0 or ep == args.epochs - 1:
            print(f"[torch] epoch {ep:4d}  loss {tot / N:.4f}")

    os.makedirs(args.out, exist_ok=True)
    ckpt = os.path.join(args.out, "diffusion_policy_torch.pt")
    torch.save(
        {
            "model": model.state_dict(),
            "manifest": manifest,
            "obs_steps": obs_steps,
            "horizon": H,
            "diffusion_steps": Tdiff,
            "mean": mean.tolist(),
            "std": std.tolist(),
        },
        ckpt,
    )
    print(f"[torch] saved checkpoint -> {ckpt}")
    return 0


# ----------------------------- lerobot backend (delegate) -------------------------------------------
def train_lerobot(manifest, args):
    try:
        import lerobot  # noqa
    except Exception as e:
        print(f"[lerobot] not installed ({e}). `pip install lerobot`.")
    print("[lerobot] To train the maintained Diffusion Policy on this data:")
    print(
        "  1) build a LeRobotDataset:  python3 ../realbot/waypoints_to_lerobot.py <demos> --lerobot --repo-id",
        args.repo_id,
    )
    print("  2) train:")
    print(
        f"     lerobot-train --policy.type=diffusion --dataset.repo_id={args.repo_id} \\"
    )
    print(
        f"        --policy.n_obs_steps={args.obs_steps} --policy.horizon={args.horizon} \\"
    )
    print(
        "        --output_dir=outputs/armsmith_diffusion --batch_size=64 --steps=200000"
    )
    print(
        f"  features: action/observation.state = {manifest['action_dim']}-dim joint+gripper degrees,"
    )
    print(f"            order {manifest['feature_names']}, fps {manifest['fps']}.")
    return 0


def main():
    ap = argparse.ArgumentParser(
        description="Train a low-dim joint-space Diffusion Policy for ARMSMITH."
    )
    ap.add_argument(
        "dataset",
        help="portable intermediate dataset dir (from waypoints_to_lerobot.py)",
    )
    ap.add_argument("--backend", choices=["torch", "lerobot"], default="torch")
    ap.add_argument(
        "--dry-run",
        action="store_true",
        help="validate + print plan, no training (no ML deps)",
    )
    ap.add_argument("-o", "--out", default="ckpt")
    ap.add_argument("--obs-steps", type=int, default=2)
    ap.add_argument("--horizon", type=int, default=8)
    ap.add_argument("--diffusion-steps", type=int, default=50)
    ap.add_argument("--epochs", type=int, default=200)
    ap.add_argument("--batch", type=int, default=64)
    ap.add_argument("--lr", type=float, default=1e-3)
    ap.add_argument("--repo-id", default="local/armsmith")
    args = ap.parse_args()

    manifest, eps = load_dataset(args.dataset)
    obs, act = make_windows(eps, args.obs_steps, args.horizon)
    print(
        f"Dataset: {manifest['num_episodes']} episodes, {manifest['total_frames']} frames, "
        f"dim={manifest['action_dim']} {manifest['feature_names']}, fps={manifest['fps']}"
    )
    print(
        f"Training windows: {len(obs)} (obs_steps={args.obs_steps}, horizon={args.horizon})"
    )
    print(
        f"Plan: backend={args.backend} epochs={args.epochs} batch={args.batch} "
        f"diffusion_steps={args.diffusion_steps} lr={args.lr} -> {args.out}/"
    )

    if args.dry_run:
        print(
            "Dry run — dataset valid, training plan above. Re-run without --dry-run to train."
        )
        return 0
    if args.backend == "torch":
        return train_torch(manifest, eps, args)
    return train_lerobot(manifest, args)


if __name__ == "__main__":
    sys.exit(main())
