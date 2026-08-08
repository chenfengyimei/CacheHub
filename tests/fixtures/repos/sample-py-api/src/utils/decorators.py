"""Retry decorator for transient failures."""
import functools
import time
import logging
from typing import Callable, TypeVar, ParamSpec

P = ParamSpec('P')
T = TypeVar('T')

logger = logging.getLogger(__name__)


def retry(max_attempts: int = 3, delay: float = 0.5, backoff: float = 2.0):
    """Retry decorator with exponential backoff.

    Args:
        max_attempts: Maximum number of retry attempts.
        delay: Initial delay between retries in seconds.
        backoff: Multiplier applied to delay between retries.

    Returns:
        Decorated function that retries on exception.
    """
    def decorator(func: Callable[P, T]) -> Callable[P, T]:
        @functools.wraps(func)
        def wrapper(*args: P.args, **kwargs: P.kwargs) -> T:
            attempt = 0
            current_delay = delay
            last_exception = None

            while attempt < max_attempts:
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    last_exception = e
                    attempt += 1
                    if attempt >= max_attempts:
                        logger.error(
                            "Function %s failed after %d attempts: %s",
                            func.__name__, attempt, e
                        )
                        raise
                    logger.warning(
                        "Attempt %d/%d for %s failed: %s. Retrying in %.1fs",
                        attempt, max_attempts, func.__name__, e, current_delay
                    )
                    time.sleep(current_delay)
                    current_delay *= backoff

            raise last_exception  # type: ignore[misc]

        return wrapper
    return decorator
