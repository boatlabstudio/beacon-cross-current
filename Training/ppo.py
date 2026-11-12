"""Small, continuous-action PPO-Clip with separate actor and value networks.

The action log probability is computed for the sampled Gaussian action. The
simulator clamps the resulting actuator command to its physical [-1, 1] range.
"""

import numpy as np


class Adam:
    def __init__(self, parameters):
        self.mean = {key: np.zeros_like(value) for key, value in parameters.items()}
        self.variance = {key: np.zeros_like(value) for key, value in parameters.items()}
        self.step = 0

    def update(self, parameters, gradients, learning_rate):
        self.step += 1
        norm = np.sqrt(sum(float(np.sum(g * g)) for g in gradients.values()))
        scale = min(1.0, 0.5 / max(norm, 1e-8))
        for key, gradient in gradients.items():
            gradient = gradient * scale
            self.mean[key] = 0.9 * self.mean[key] + 0.1 * gradient
            self.variance[key] = 0.999 * self.variance[key] + 0.001 * gradient**2
            mean = self.mean[key] / (1 - 0.9**self.step)
            variance = self.variance[key] / (1 - 0.999**self.step)
            parameters[key] -= learning_rate * mean / (np.sqrt(variance) + 1e-8)


class Policy:
    def __init__(self, observation_size, rng, hidden=48):
        self.rng = rng
        self.parameters = {}
        for prefix, output, output_scale in (("actor", 2, 0.035), ("critic", 1, 0.55)):
            self.parameters.update({
                prefix + "_w1": rng.normal(0, np.sqrt(1 / observation_size), (observation_size, hidden)).astype(np.float32),
                prefix + "_b1": np.zeros(hidden, dtype=np.float32),
                prefix + "_w2": rng.normal(0, output_scale / np.sqrt(hidden), (hidden, output)).astype(np.float32),
                prefix + "_b2": np.zeros(output, dtype=np.float32),
            })
        self.parameters["log_std"] = np.full(2, -0.48, dtype=np.float32)
        self.optimizer = Adam(self.parameters)

    def forward(self, obs, name):
        p = self.parameters
        hidden = np.tanh(obs @ p[name + "_w1"] + p[name + "_b1"])
        result = hidden @ p[name + "_w2"] + p[name + "_b2"]
        return hidden, result

    def mean(self, obs):
        return self.forward(obs, "actor")[1]

    def values(self, obs):
        return self.forward(obs, "critic")[1][:, 0]

    def log_probability(self, action, mean):
        log_std = self.parameters["log_std"]
        residual = (action - mean) * np.exp(-log_std)
        return -np.sum(0.5 * residual**2 + log_std + 0.5 * np.log(2 * np.pi), axis=1)

    def sample(self, obs):
        mean = self.mean(obs)
        noise = self.rng.normal(size=mean.shape).astype(np.float32)
        actions = mean + noise * np.exp(self.parameters["log_std"])
        return actions, self.log_probability(actions, mean), self.values(obs)

    def update(self, obs, actions, old_logp, advantage, returns, learning_rate,
               epochs=5, batch_size=256, clip=0.20, entropy_bonus=0.003):
        advantage = (advantage - advantage.mean()) / (advantage.std() + 1e-8)
        for _ in range(epochs):
            order = self.rng.permutation(len(obs))
            for start in range(0, len(obs), batch_size):
                ids = order[start:start + batch_size]
                x, a, adv = obs[ids], actions[ids], advantage[ids]
                actor_hidden, mean = self.forward(x, "actor")
                value_hidden, value = self.forward(x, "critic")
                ratio = np.exp(np.clip(self.log_probability(a, mean) - old_logp[ids], -18, 18))
                flat = ((adv > 0) & (ratio > 1 + clip)) | ((adv < 0) & (ratio < 1 - clip))
                dlogp = np.where(flat, 0, -adv * ratio / len(ids))[:, None]
                inverse_variance = np.exp(-2 * self.parameters["log_std"])
                diff = a - mean
                gradients = self.backward(x, actor_hidden, dlogp * diff * inverse_variance, "actor")
                gradients["log_std"] = np.sum(dlogp * (diff**2 * inverse_variance - 1), axis=0) - entropy_bonus
                gradients.update(self.backward(
                    x, value_hidden, 0.5 * (value - returns[ids, None]) / len(ids), "critic"
                ))
                self.optimizer.update(self.parameters, gradients, learning_rate)
                np.clip(self.parameters["log_std"], -2.8, 0.1, out=self.parameters["log_std"])

    def backward(self, obs, hidden, gradient, name):
        delta = (gradient @ self.parameters[name + "_w2"].T) * (1 - hidden**2)
        return {
            name + "_w2": hidden.T @ gradient,
            name + "_b2": gradient.sum(axis=0),
            name + "_w1": obs.T @ delta,
            name + "_b1": delta.sum(axis=0),
        }

    def save(self, path):
        np.savez_compressed(path, **self.parameters)


def gae(rewards, ends, values, last_values, gamma=0.99, lam=0.95):
    """The task deadline is a terminal failure, like missing the gate."""
    advantages = np.zeros_like(values)
    carry = np.zeros_like(last_values)
    for t in range(len(values) - 1, -1, -1):
        following = last_values if t + 1 == len(values) else values[t + 1]
        alive = 1 - ends[t]
        delta = rewards[t] + gamma * following * alive - values[t]
        carry = delta + gamma * lam * alive * carry
        advantages[t] = carry
    return advantages, advantages + values
