import warnings
warnings.filterwarnings('ignore')

from mlagents.trainers.torch_entities.model_serialization import ModelSerializer
from mlagents.trainers.torch_entities.policy import TorchPolicy
from mlagents_envs.base_env import ActionSpec, ObservationSpec, ObservationType, DimensionProperty, BehaviorSpec
from mlagents.trainers.settings import NetworkSettings, TrainerSettings
import torch

obs_specs = [
    ObservationSpec(
        shape=(225,),
        dimension_property=(DimensionProperty.NONE,),
        observation_type=ObservationType.DEFAULT,
        name="obs"
    )
]

action_spec = ActionSpec(
    continuous_size=0,
    discrete_branches=(4, 10)
)

behavior_spec = BehaviorSpec(
    observation_specs=obs_specs,
    action_spec=action_spec
)

network_settings = NetworkSettings(
    normalize=True,
    hidden_units=512,
    num_layers=3
)

policy = TorchPolicy(
    seed=0,
    behavior_spec=behavior_spec,
    network_settings=network_settings
)

checkpoint = torch.load(
    r"results\tetris_12env\Tetris\Tetris-149999.pt",
    map_location="cpu"
)
policy.load_state_dict(checkpoint["Policy"])

serializer = ModelSerializer(policy)
serializer.export_policy_model(r"results\tetris_12env\Tetris\Tetris-149999.onnx")
print("Done!")
