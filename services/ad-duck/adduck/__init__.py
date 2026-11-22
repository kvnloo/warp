"""ad-duck: privacy-first TV ad detector and volume ducker.

Only ``adduck.features.extract_features`` reads raw audio. Everything else
works on 2 Hz ``Features`` records that cannot carry words.
"""

from .detect import AdDetector, DetectorConfig
from .engine import Engine
from .features import BLOCK_S, Features
from .state import AdStateMachine, StateConfig
from .tv import Ducker, DuckerConfig, DryRunTV, FakeTV

__all__ = [
    "AdDetector",
    "AdStateMachine",
    "BLOCK_S",
    "DetectorConfig",
    "DryRunTV",
    "Ducker",
    "DuckerConfig",
    "Engine",
    "FakeTV",
    "Features",
    "StateConfig",
]
