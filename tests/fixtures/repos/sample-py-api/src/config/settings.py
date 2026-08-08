"""Application settings."""
import os
from dataclasses import dataclass


@dataclass
class Settings:
    db_url: str = os.getenv("DATABASE_URL", "sqlite:///app.db")
    db_retry_attempts: int = int(os.getenv("DB_RETRY_ATTEMPTS", "3"))
    db_retry_delay: float = float(os.getenv("DB_RETRY_DELAY", "0.5"))
    secret_key: str = os.getenv("SECRET_KEY", "dev-secret")
    debug: bool = os.getenv("DEBUG", "false").lower() == "true"


settings = Settings()
