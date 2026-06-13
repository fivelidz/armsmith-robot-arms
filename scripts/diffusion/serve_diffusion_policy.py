#!/usr/bin/env python3
"""
serve_diffusion_policy.py — DF4: serve a trained ARMSMITH Diffusion Policy over a TCP socket.

Loads a checkpoint from train_diffusion_policy.py (torch backend) and, on each request, runs the
REVERSE diffusion process (DDPM denoising) to sample an action CHUNK conditioned on the observation
the client sends. Unity polls this each decision and executes the first few actions (receding horizon),
exactly the Diffusion Policy deployment recipe (research/diffusion_pathfinding/REPORT.md §1-2,6).

Wire protocol (newline-delimited JSON over TCP, default 127.0.0.1:6020):
  request : {"obs": [[j0..jn(+grip)], ...]}   # obs_steps frames of joint+gripper degrees
  response: {"action": [[...], ...], "horizon": H, "dim": D}   # H future joint+gripper-degree targets
  also    : {"ping": true} -> {"ok": true, "horizon": H, "dim": D, "features": [...]}

Lazy-imports torch so --help / --check work without it. If no checkpoint / torch, the server can run in
--echo mode (returns the last obs repeated) so the Unity client path is testable end-to-end without ML.

Usage:
  python3 serve_diffusion_policy.py ckpt/diffusion_policy_torch.pt           # serve real policy
  python3 serve_diffusion_policy.py --echo                                   # ML-free smoke server
  python3 serve_diffusion_policy.py ckpt/...pt --check                       # load + 1 sample, print, exit
"""

import argparse, json, socket, sys, threading


class Policy:
    """Wraps the trained DDPM denoiser for sampling action chunks. Lazy torch import."""

    def __init__(self, ckpt_path):
        import torch, torch.nn as nn

        self.torch = torch
        ck = torch.load(ckpt_path, map_location="cpu", weights_only=False)
        self.manifest = ck["manifest"]
        self.obs_steps = ck["obs_steps"]
        self.H = ck["horizon"]
        self.Tdiff = ck["diffusion_steps"]
        self.dim = self.manifest["action_dim"]
        self.mean = torch.tensor(ck["mean"])
        self.std = torch.tensor(ck["std"])
        self.features = self.manifest["feature_names"]

        # rebuild the EXACT denoiser architecture from the trainer (must match key names: the trainer's
        # Denoiser wraps a `self.net = nn.Sequential(...)`, so checkpoint keys are "net.0.weight" etc.).
        cond = self.obs_steps * self.dim
        ad = self.H * self.dim

        class Denoiser(nn.Module):
            def __init__(self):
                super().__init__()
                self.net = nn.Sequential(
                    nn.Linear(cond + ad + 1, 256),
                    nn.SiLU(),
                    nn.Linear(256, 256),
                    nn.SiLU(),
                    nn.Linear(256, ad),
                )

            def forward(self, a_noisy, c, k):
                return self.net(torch.cat([a_noisy, c, k], -1))

        self.model = Denoiser()
        self.model.load_state_dict(ck["model"])
        self.model.eval()

        betas = torch.linspace(1e-4, 0.02, self.Tdiff)
        self.alpha_bar = torch.cumprod(1 - betas, 0)
        self.betas = betas

    def _norm(self, arr):
        t = self.torch.tensor(arr, dtype=self.torch.float32)
        return (t - self.mean) / self.std

    def _denorm(self, t):
        return t * self.std + self.mean

    def sample(self, obs):
        """obs: list of obs_steps frames (joint+gripper deg). Returns action chunk [H][dim] in degrees."""
        torch = self.torch
        with torch.no_grad():
            # pad/trim obs to obs_steps
            frames = list(obs)[-self.obs_steps :]
            while len(frames) < self.obs_steps:
                frames.insert(0, frames[0])
            cond = torch.cat([self._norm(f) for f in frames]).unsqueeze(
                0
            )  # [1, obs_steps*dim]
            ad = self.H * self.dim
            a = torch.randn(1, ad)  # start from noise
            for k in reversed(range(self.Tdiff)):
                kf = torch.tensor([[k / self.Tdiff]], dtype=torch.float32)
                eps = self.model(a, cond, kf)
                ab = self.alpha_bar[k]
                # predict x0 then take a DDPM-ish step toward it
                a0 = (a - torch.sqrt(1 - ab) * eps) / torch.sqrt(ab)
                if k > 0:
                    ab_prev = self.alpha_bar[k - 1]
                    a = torch.sqrt(ab_prev) * a0 + torch.sqrt(1 - ab_prev) * eps
                else:
                    a = a0
            a = a.reshape(self.H, self.dim)
            a = self._denorm(a)
            return a.tolist()


def handle_client(conn, policy, echo):
    f = conn.makefile("rwb")
    try:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                req = json.loads(line)
            except Exception:
                f.write(b'{"error":"bad json"}\n')
                f.flush()
                continue
            if req.get("ping"):
                if policy:
                    resp = {
                        "ok": True,
                        "horizon": policy.H,
                        "dim": policy.dim,
                        "features": policy.features,
                    }
                else:
                    resp = {"ok": True, "echo": True}
            else:
                obs = req.get("obs", [])
                if policy:
                    chunk = policy.sample(obs)
                    resp = {"action": chunk, "horizon": policy.H, "dim": policy.dim}
                else:
                    # echo mode: repeat the last obs frame H times (ML-free path test)
                    last = obs[-1] if obs else [0, 0, 0, 0, 0]
                    resp = {
                        "action": [last for _ in range(8)],
                        "horizon": 8,
                        "dim": len(last),
                    }
            f.write((json.dumps(resp) + "\n").encode())
            f.flush()
    except Exception as e:
        sys.stderr.write(f"[serve] client error: {e}\n")
    finally:
        try:
            conn.close()
        except Exception:
            pass


def main():
    ap = argparse.ArgumentParser(
        description="Serve a trained ARMSMITH Diffusion Policy over TCP."
    )
    ap.add_argument(
        "ckpt",
        nargs="?",
        help="checkpoint .pt from train_diffusion_policy.py (torch backend)",
    )
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=6020)
    ap.add_argument(
        "--echo",
        action="store_true",
        help="run ML-free echo server (no ckpt/torch needed)",
    )
    ap.add_argument(
        "--check", action="store_true", help="load + 1 sample, print, exit (no server)"
    )
    args = ap.parse_args()

    policy = None
    if not args.echo:
        if not args.ckpt:
            print("Provide a checkpoint, or use --echo. (--help for usage)")
            return 2
        try:
            policy = Policy(args.ckpt)
            print(
                f"[serve] loaded {args.ckpt} | horizon={policy.H} dim={policy.dim} "
                f"obs_steps={policy.obs_steps} features={policy.features}"
            )
        except Exception as e:
            print(f"[serve] could not load policy ({e}). Falling back to --echo mode.")
            policy = None

    if args.check:
        if policy:
            obs = [policy.manifest["stats"]["mean"]]  # a plausible observation
            chunk = policy.sample(obs)
            print(f"[serve] sample action chunk: {len(chunk)}x{len(chunk[0])}")
            print(f"  first action: {[round(x, 1) for x in chunk[0]]}")
            print(f"  last  action: {[round(x, 1) for x in chunk[-1]]}")
        else:
            print("[serve] --check needs a loadable checkpoint.")
        return 0

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(4)
    print(
        f"[serve] diffusion policy server on {args.host}:{args.port} "
        f"({'echo' if policy is None else 'real policy'}). Ctrl-C to stop."
    )
    try:
        while True:
            conn, _ = srv.accept()
            threading.Thread(
                target=handle_client, args=(conn, policy, args.echo), daemon=True
            ).start()
    except KeyboardInterrupt:
        print("\n[serve] shutting down.")
    finally:
        srv.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
