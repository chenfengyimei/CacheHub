"""Pipeline configuration."""
import os
from dataclasses import dataclass


@dataclass
class PipelineConfig:
    batch_size: int = 100
    max_retries: int = 3
    required_fields: list = None
    output_format: str = "json"

    def __post_init__(self):
        if self.required_fields is None:
            self.required_fields = ["id", "timestamp", "data"]
