# Tetris-AI-Game

>

# Tetris RL Agent (Unity ML-Agents)

A Reinforcement Learning agent trained to play Tetris using **Unity ML-Agents (PPO)**.

This project demonstrates deep reinforcement learning applied to a classic game environment, including reward shaping, environment design, and neural network training using Proximal Policy Optimization (PPO).

---

## Project Overview

The agent interacts with a custom Tetris environment built in Unity and learns to:

* Move pieces left/right
* Rotate pieces
* Hard drop pieces
* Clear lines
* Avoid game over

Training is performed using Unity ML-Agents with a discrete action space and grid-based observations.

---

## Reinforcement Learning Setup

### Algorithm

* PPO (Proximal Policy Optimization)

### Observation Space

* Binary grid representation of the board
* Each cell is encoded as:

  * `1` → occupied
  * `0` → empty

### Action Space (Discrete)

| Action | Description |
| ------ | ----------- |
| 0      | Move Left   |
| 1      | Move Right  |
| 2      | Rotate      |
| 3      | Hard Drop   |

---

## Reward Design

The agent receives:

* `+1 × linesCleared` → for clearing lines
* `-0.001` → small time penalty per step
* `-1` → game over

This reward shaping encourages:

* Clearing lines
* Avoiding early death
* Efficient gameplay

---

## Training Configuration

```yaml
trainer_type: ppo
max_steps: 500000
batch_size: 64
buffer_size: 2048
learning_rate: 3.0e-4
num_layers: 2
hidden_units: 128
gamma: 0.99
time_horizon: 128
```

---

## How to Train

### 1️. Activate Environment

```bash
conda activate mlagents
```

### 2️. Start Training

```bash
mlagents-learn tetris_config.yaml --run-id=tetris
```

### 3️. Press ▶ Play in Unity

Training logs will appear in the terminal.

---

## Example Training Output

```
Step: 30000
Mean Reward: -1.173
```

As training progresses, mean reward should improve as the agent learns better placement strategies.

---

## Tech Stack

* Unity
* Unity ML-Agents (v4.0.1)
* Python 3.10
* PyTorch
* PPO Algorithm

---

## Project Structure

```
Assets/
  Scripts/
    TetrisAgent.cs
    Board.cs
tetris_config.yaml
```

---

## Future Improvements

* Reward shaping for height control and hole minimization
* Curriculum learning
* Convolutional observation encoding
* Self-play or competitive mode
* Hyperparameter tuning

---

## Author

Nisy, 
Master’s Student in Artificial Intelligence
Junia ISEN, Lille

