"""Network and loss definitions from the FNN / PINN scripts."""
from __future__ import annotations

import torch
import torch.nn as nn


class ResidualBlock(nn.Module):
    def __init__(self, dim: int, dropout: float):
        super().__init__()
        self.fc1 = nn.Linear(dim, dim)
        self.act = nn.LeakyReLU(0.01)
        self.dropout = nn.Dropout(dropout)
        self.norm = nn.LayerNorm(dim)

    def forward(self, x):
        out = self.dropout(self.act(self.fc1(x)))
        out = out + x
        return self.act(self.norm(out))


class FNNWithResidual(nn.Module):
    """Input FC -> LeakyReLU -> dropout -> N residual blocks -> output FC."""

    def __init__(self, input_dim: int, hidden_dim: int, num_blocks: int, output_dim: int, dropout: float):
        super().__init__()
        self.input_fc = nn.Linear(input_dim, hidden_dim)
        self.act = nn.LeakyReLU(0.01)
        self.dropout = nn.Dropout(dropout)
        self.blocks = nn.ModuleList([ResidualBlock(hidden_dim, dropout) for _ in range(num_blocks)])
        self.output_fc = nn.Linear(hidden_dim, output_dim)

    def forward(self, x):
        out = self.dropout(self.act(self.input_fc(x)))
        for b in self.blocks:
            out = b(out)
        return self.output_fc(out)


class TrainableL1L2Loss(nn.Module):
    """alpha * L1 + (1 - alpha) * L2 with a learnable alpha, plus a box-constraint penalty."""

    def __init__(self, initial_alpha: float, min_constraint, max_constraint, penalty_weight: float):
        super().__init__()
        self.alpha = nn.Parameter(torch.tensor(float(initial_alpha)))
        self.l1 = nn.L1Loss()
        self.l2 = nn.MSELoss()
        self.min_constraint = min_constraint
        self.max_constraint = max_constraint
        self.penalty_weight = penalty_weight

    def forward(self, preds, targets):
        alpha = torch.clamp(self.alpha, 1e-6, 1.0)
        loss = alpha * self.l1(preds, targets) + (1 - alpha) * self.l2(preds, targets)
        penalty = preds.new_zeros(())
        if self.min_constraint is not None:
            penalty = penalty + torch.sum(torch.relu(self.min_constraint - preds))
        if self.max_constraint is not None:
            penalty = penalty + torch.sum(torch.relu(preds - self.max_constraint))
        return loss + self.penalty_weight * penalty


class CompositeLoss(nn.Module):
    """PINN loss: L1L2 on I plus relative-error terms on deflections and rotations."""

    def __init__(self, nelem: int, initial_alpha: float, min_constraint, max_constraint,
                 penalty_weight: float, penalty_pinn: float):
        super().__init__()
        self.nelem = nelem
        self.penalty_pinn = penalty_pinn
        self.l1l2 = TrainableL1L2Loss(initial_alpha, min_constraint, max_constraint, penalty_weight)

    @property
    def alpha(self):
        return self.l1l2.alpha

    def forward(self, preds, targets):
        n = self.nelem
        d = n + 1
        loss_I = self.l1l2(preds[:, :n], targets[:, :n])
        eps = 1e-8
        defl = torch.mean(torch.abs(preds[:, n:n + d] - targets[:, n:n + d]) / (torch.abs(targets[:, n:n + d]) + eps))
        rot = torch.mean(torch.abs(preds[:, n + d:] - targets[:, n + d:]) / (torch.abs(targets[:, n + d:]) + eps))
        return loss_I + self.penalty_pinn * (defl + rot)
